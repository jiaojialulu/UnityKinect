using UnityEngine;
using System.Collections;
using Windows.Kinect;
using System.Collections.Generic;


struct tri2
{
    public Vector3 p1;          // 12
    public Vector3 p2;          // 12
    public Vector3 p3;          // 12
};


public class newMesh2: MonoBehaviour
{

    public GameObject MultiSourceManager;
    public ComputeShader _Shader;
    public float maxDepthLimit;
    public float minDepthLimit;
    public float distanceThreshold;

    private KinectSensor _Sensor;
    private CoordinateMapper _Mapper;
    private Mesh _Mesh;
    private Vector3[] _Vertices;
    private Vector2[] _UV;
    private int[] _Triangles;
    // mesh模板，存的是降采样后各个顶点的一维下标
    private int[] _TrianglesTemplate;
    // mesh模板，存的是降采样前各个顶点的一维下标
    private int[] _TrianglesTemplateSource;

    // Only works at 4 right now
    private const int _DownsampleSize = 4;
    private const double _DepthScale = 0.1f;
    private const int _Speed = 50;

    private int _NumThread;
    private ComputeBuffer _Buffer;
    private MultiSourceManager _MultiManager;
    private CameraIntrinsics _CameraIntrinsics = new CameraIntrinsics();
    private float[] colmap;
    private float[] rowmap;
    void Start()
    {
        int width = 0, height = 0;
        _Sensor = KinectSensor.GetDefault();
        if (_Sensor != null)
        {
            _Mapper = _Sensor.CoordinateMapper;
            _CameraIntrinsics = _Mapper.GetDepthCameraIntrinsics();

            var frameDesc = _Sensor.DepthFrameSource.FrameDescription;
            width = frameDesc.Width;
            height = frameDesc.Height;
            // Downsample to lower resolution
            _TrianglesTemplate = Init(frameDesc.Width / _DownsampleSize, frameDesc.Height / _DownsampleSize);

            if (!_Sensor.IsOpen)
            {
                _Sensor.Open();
            }
        }
        // must be greater than 0, less or equal to 2048 and a multiple of 4.
        _Buffer = new ComputeBuffer(_TrianglesTemplate.Length / 3, 36);

        // 初始化参数
        colmap = new float[512];
        rowmap = new float[424];

        for (int i = 0; i < 512; i++)
            colmap[i] = (i - _CameraIntrinsics.PrincipalPointX + 0.5f) / _CameraIntrinsics.FocalLengthX;
        for (int i = 0; i < 424; i++)
            rowmap[i] = (i - _CameraIntrinsics.PrincipalPointY + 0.5f) / _CameraIntrinsics.FocalLengthY;

        _Shader.SetInt("width", width);
        _Shader.SetInt("height", height);
        _Shader.SetInt("downSampleSize", _DownsampleSize);
        _Shader.SetFloat("cx", _CameraIntrinsics.PrincipalPointX);
        _Shader.SetFloat("cy", _CameraIntrinsics.PrincipalPointY);
        _Shader.SetFloat("fx", _CameraIntrinsics.FocalLengthX);
        _Shader.SetFloat("fy", _CameraIntrinsics.FocalLengthY);
        _Shader.SetFloat("maxDepthLimit", maxDepthLimit);
        _Shader.SetFloat("minDepthLimit", minDepthLimit);
        _Shader.SetFloat("distanceThreshold", distanceThreshold);
        //_Shader.SetFloats("colmap", colmap);
        //_Shader.SetFloats("rowmap", rowmap);
        _NumThread = _TrianglesTemplate.Length / 3 / 8;
    }

    int[] Init(int width, int height)
    {
        _Mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = _Mesh;

        _Vertices = new Vector3[width * height];
        _UV = new Vector2[width * height];
        _Triangles = new int[6 * ((width - 1) * (height - 1))];
        _TrianglesTemplateSource = new int[6 * ((width - 1) * (height - 1))];

        int triangleIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width) + x;

                _Vertices[index] = new Vector3(x, -y, 0);
                _UV[index] = new Vector2(((float)x / (float)width), ((float)y / (float)height));

                // Skip the last row/col
                if (x != (width - 1) && y != (height - 1))
                {
                    int topLeft = index;
                    int topRight = topLeft + 1;
                    int bottomLeft = topLeft + width;
                    int bottomRight = bottomLeft + 1;

                    _Triangles[triangleIndex++] = topLeft;
                    _Triangles[triangleIndex++] = bottomLeft;
                    _Triangles[triangleIndex++] = topRight;
                    _Triangles[triangleIndex++] = bottomLeft;
                    _Triangles[triangleIndex++] = bottomRight;
                    _Triangles[triangleIndex++] = topRight;

                    _TrianglesTemplateSource[triangleIndex - 6] = IndexTrans(topLeft, _DownsampleSize, 512, 424);
                    _TrianglesTemplateSource[triangleIndex - 5] = IndexTrans(bottomLeft, _DownsampleSize, 512, 424);
                    _TrianglesTemplateSource[triangleIndex - 4] = IndexTrans(topRight, _DownsampleSize, 512, 424);
                    _TrianglesTemplateSource[triangleIndex - 3] = IndexTrans(bottomLeft, _DownsampleSize, 512, 424);
                    _TrianglesTemplateSource[triangleIndex - 2] = IndexTrans(bottomRight, _DownsampleSize, 512, 424);
                    _TrianglesTemplateSource[triangleIndex - 1] = IndexTrans(topRight, _DownsampleSize, 512, 424);
                }
            }
        }

        //_Mesh.vertices = _Vertices;
        ////_Mesh.uv = _UV;
        //_Mesh.triangles = _Triangles;
        //_Mesh.RecalculateNormals();
        return _Triangles;
    }

    void OnGUI()
    {
        GUI.BeginGroup(new Rect(0, 0, Screen.width, Screen.height));
        GUI.EndGroup();
    }

    void Update()
    {
        if (_Sensor == null)
        {
            return;
        }

        float yVal = Input.GetAxis("Horizontal");
        float xVal = -Input.GetAxis("Vertical");

        transform.Rotate(
            (xVal * Time.deltaTime * _Speed),
            (yVal * Time.deltaTime * _Speed),
            0,
            Space.Self);

        if (MultiSourceManager == null)
        {
            return;
        }

        _MultiManager = MultiSourceManager.GetComponent<MultiSourceManager>();
        if (_MultiManager == null)
        {
            return;
        }

        gameObject.GetComponent<Renderer>().material.mainTexture = _MultiManager.GetColorTexture();

        RefreshData(_MultiManager.GetDepthData(),
                    _MultiManager.ColorWidth,
                    _MultiManager.ColorHeight);
    }

    /// <summary>
    /// 降采样后一维坐标转换为原始图片的一维下标
    /// </summary>
    /// <param name="sampleIndex"></param>
    /// <param name="_DownsampleSize"></param>
    /// <param name="width">原始图片宽</param>
    /// <param name="height">原始图片高</param>
    /// <returns></returns>
    private int IndexTrans(int sampleIndex, int _DownsampleSize, int width, int height)
    {
        int indexX = width / _DownsampleSize;
        int indexY = height / _DownsampleSize;
        int result = sampleIndex / indexX * width * _DownsampleSize + sampleIndex % indexX * _DownsampleSize;
        return result;
    }
    private void RefreshData(ushort[] depthData, int colorWidth, int colorHeight)
    {
        var frameDesc = _Sensor.DepthFrameSource.FrameDescription;

        // 映射深度图到彩色图
        ColorSpacePoint[] colorSpace = new ColorSpacePoint[depthData.Length];
        _Mapper.MapDepthFrameToColorSpace(depthData, colorSpace);

        // shader输入参数，数目为三角形数
        tri2[] values = new tri2[_TrianglesTemplate.Length / 3];

        //int a = 0;
        for (int i = 0; i < _TrianglesTemplate.Length / 3; i++)
        {
            // (x,y,z):x代表顶点在原始图片中的下标，z代表深度
            values[i].p1 = new Vector3(_TrianglesTemplateSource[i * 3 + 0], 0, (int)depthData[_TrianglesTemplateSource[i * 3 + 0]] / 1000.0f);
            values[i].p2 = new Vector3(_TrianglesTemplateSource[i * 3 + 1], 0, (int)depthData[_TrianglesTemplateSource[i * 3 + 1]] / 1000.0f);
            values[i].p3 = new Vector3(_TrianglesTemplateSource[i * 3 + 2], 0, (int)depthData[_TrianglesTemplateSource[i * 3 + 2]] / 1000.0f);
        }

        // compute shader计算
        _Buffer.SetData(values);
        Dispatch(frameDesc.Width, frameDesc.Height);

        // 根据Shader返回的buffer数据更新mesh
        tri2[] result = new tri2[_TrianglesTemplate.Length / 3];
        _Buffer.GetData(result);

        List<Vector3> processVertices = new List<Vector3>();
        List<Vector2> processUVs = new List<Vector2>();
        List<int> processTriangles = new List<int>();
        //int index = 0;

        for (int i = 0; i < result.Length; i++)
        {
            if ((result[i].p1 == Vector3.zero) || (result[i].p2 == Vector3.zero) || (result[i].p3 == Vector3.zero))
            {
                continue;
            }

            // vertices
            //processVertices.Add(result[i].p1);
            //processVertices.Add(result[i].p2);
            //processVertices.Add(result[i].p3);

            _Vertices[_TrianglesTemplate[i * 3 + 0]] = result[i].p1;
            _Vertices[_TrianglesTemplate[i * 3 + 1]] = result[i].p2;
            _Vertices[_TrianglesTemplate[i * 3 + 2]] = result[i].p3;

            // uvs
            var colorSpacePoint = colorSpace[_TrianglesTemplateSource[i * 3 + 0]];
            _UV[_TrianglesTemplate[i * 3 + 0]] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);
            //processUVs.Add(new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight));
            colorSpacePoint = colorSpace[_TrianglesTemplateSource[i * 3 + 1]];
            _UV[_TrianglesTemplate[i * 3 + 1]] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);
            //processUVs.Add(new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight));
            colorSpacePoint = colorSpace[_TrianglesTemplateSource[i * 3 + 2]];
            _UV[_TrianglesTemplate[i * 3 + 2]] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);
            //processUVs.Add(new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight));

            // triangles
            processTriangles.Add(_TrianglesTemplate[i * 3 + 0]);
            processTriangles.Add(_TrianglesTemplate[i * 3 + 1]);
            processTriangles.Add(_TrianglesTemplate[i * 3 + 2]);
            //processTriangles.Add(index++);
            //processTriangles.Add(index++);
            //processTriangles.Add(index++);
            //_Triangles[i * 3 + 0] = result[i].index1;
            //_Triangles[i * 3 + 1] = result[i].index2;
            //_Triangles[i * 3 + 2] = result[i].index3;
        }
        //print(a+" : "+b);

        //int[] indecies = new int[processVertices.Count];
        //for (int i = 0; i < processVertices.Count; ++i)
        //{
        //    indecies[i] = i;
        //}

        _Mesh.Clear();
        // final
        //_Mesh.vertices = processVertices.ToArray();
        //_Mesh.uv = processUVs.ToArray();
        //_Mesh.triangles = processTriangles.ToArray();
        //_Mesh.SetIndices(indecies, MeshTopology.Points, 0);
        //_Mesh.RecalculateNormals();

        _Mesh.vertices = _Vertices;
        _Mesh.uv = _UV;
        _Mesh.triangles = processTriangles.ToArray();
        ////_Mesh.colors
        //_Mesh.triangles = _Triangles;
        ////_Mesh.SetIndices(indecies, MeshTopology.Points, 0);
        ////_Mesh.RecalculateNormals();
    }

    void Dispatch(int width, int height)
    {
        
        //_Shader.SetInt("numThread",numThread);

        int kid = _Shader.FindKernel("CSMain");
        _Shader.SetBuffer(kid, "buffer", _Buffer);
        _Shader.Dispatch(kid, _NumThread, 1, 1);
    }

    void OnApplicationQuit()
    {
        if (_Mapper != null)
        {
            _Mapper = null;
        }

        if (_Sensor != null)
        {
            if (_Sensor.IsOpen)
            {
                _Sensor.Close();
            }

            _Sensor = null;
        }
    }
}
