using UnityEngine;
using System.Collections;
using Windows.Kinect;

struct tri
{
    public float depth1;       // 4
    public float depth2;       // 4
    public float depth3;       // 4
    public int index1;          // 4
    public int index2;          // 4
    public int index3;          // 4
    public Vector3 p1;          // 12
    public Vector3 p2;          // 12
    public Vector3 p3;          // 12
};

public class newMesh : MonoBehaviour
{

    public GameObject MultiSourceManager;
    
    private KinectSensor _Sensor;
    private CoordinateMapper _Mapper;
    private Mesh _Mesh;
    private Vector3[] _Vertices;
    private Vector2[] _UV;
    private int[] _Triangles;
    // mesh模板
    private int[] _TrianglesTemplate;

    // Only works at 4 right now
    private const int _DownsampleSize = 4;
    private const double _DepthScale = 0.1f;
    private const int _Speed = 50;

    public ComputeShader _Shader;
    private ComputeBuffer _Buffer;
    private MultiSourceManager _MultiManager;
    private CameraIntrinsics _CameraIntrinsics = new CameraIntrinsics();

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
            _TrianglesTemplate = CreateMesh(frameDesc.Width / _DownsampleSize, frameDesc.Height / _DownsampleSize);

            if (!_Sensor.IsOpen)
            {
                _Sensor.Open();
            }
        }
        // must be greater than 0, less or equal to 2048 and a multiple of 4.
        _Buffer = new ComputeBuffer(_TrianglesTemplate.Length/6, 60);

      
    }

    int[] CreateMesh(int width, int height)
    {
        _Mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = _Mesh;

        _Vertices = new Vector3[width * height];
        _UV = new Vector2[width * height];
        _Triangles = new int[6 * ((width - 1) * (height - 1))];

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
                    _Triangles[triangleIndex++] = topRight;
                    _Triangles[triangleIndex++] = bottomLeft;
                    _Triangles[triangleIndex++] = bottomLeft;
                    _Triangles[triangleIndex++] = topRight;
                    _Triangles[triangleIndex++] = bottomRight;
                }
            }
        }

        _Mesh.vertices = _Vertices;
        _Mesh.uv = _UV;
        _Mesh.triangles = _Triangles;
        _Mesh.RecalculateNormals();
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

    private int IndexTrans(int sampleIndex,int _DownsampleSize,int width,int height)
    {
        int indexX = width / _DownsampleSize;
        int indexY = height / _DownsampleSize;
        int result = sampleIndex / indexX * width*_DownsampleSize + sampleIndex % indexX * _DownsampleSize;
        return result;
    }
    private void RefreshData(ushort[] depthData, int colorWidth, int colorHeight)
    {
        var frameDesc = _Sensor.DepthFrameSource.FrameDescription;

        ColorSpacePoint[] colorSpace = new ColorSpacePoint[depthData.Length];
        _Mapper.MapDepthFrameToColorSpace(depthData, colorSpace);

        // shader输入参数
        tri[] values = new tri[_TrianglesTemplate.Length / 3];

        for (int i = 0; i < _TrianglesTemplate.Length/3;i++ )
        {
            values[i].index1 = _TrianglesTemplate[i * 3 + 0];
            values[i].index2 = _TrianglesTemplate[i * 3 + 1];
            values[i].index3 = _TrianglesTemplate[i * 3 + 2];

            values[i].depth1 = (int)depthData[IndexTrans(values[i].index1, _DownsampleSize, frameDesc.Width, frameDesc.Height)]/1000.0f;
            values[i].depth2 = (int)depthData[IndexTrans(values[i].index2, _DownsampleSize, frameDesc.Width, frameDesc.Height)]/1000.0f;
            values[i].depth3 = (int)depthData[IndexTrans(values[i].index3, _DownsampleSize, frameDesc.Width, frameDesc.Height)]/1000.0f;

            values[i].p1 = new Vector3(0, 0, 0);
            values[i].p2 = new Vector3(0, 0, 0);
            values[i].p3 = new Vector3(0, 0, 0);
        }

        // compute shader计算
        _Buffer.SetData(values);
        Dispatch(frameDesc.Width, frameDesc.Height);

        // 根据Shader返回的buffer数据更新mesh
        tri[] result = new tri[_TrianglesTemplate.Length/3];
        _Buffer.GetData(result);

        _Vertices = new Vector3[_TrianglesTemplate.Length];
        _Vertices = new Vector3[_TrianglesTemplate.Length];

        for (int i = 0; i < result.Length;i++ )
        {
            _Vertices[i * 3 + 0] = result[i].p1;
            _Vertices[i * 3 + 1] = result[i].p2;
            _Vertices[i * 3 + 2] = result[i].p3;

            var colorSpacePoint = colorSpace[IndexTrans(result[i].index1, _DownsampleSize, frameDesc.Width, frameDesc.Height)];
            _UV[i * 3 + 0] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);
            colorSpacePoint = colorSpace[IndexTrans(result[i].index2, _DownsampleSize, frameDesc.Width, frameDesc.Height)];
            _UV[i * 3 + 1] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);
            colorSpacePoint = colorSpace[IndexTrans(result[i].index3, _DownsampleSize, frameDesc.Width, frameDesc.Height)];
            _UV[i * 3 + 2] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);

            _Triangles[i * 3 + 0] = result[i].index1;
            _Triangles[i * 3 + 1] = result[i].index2;
            _Triangles[i * 3 + 2] = result[i].index3;
        }

        _Mesh.vertices = _Vertices;
        _Mesh.uv = _UV;
        _Mesh.triangles = _Triangles;
        _Mesh.RecalculateNormals();
    }

    void Dispatch(int width,int height)
    {
        _Shader.SetInt("width", width);
        _Shader.SetInt("height", height);
        _Shader.SetInt("downSampleSize", _DownsampleSize);
        _Shader.SetFloat("cx", _CameraIntrinsics.PrincipalPointX);
        _Shader.SetFloat("cy", _CameraIntrinsics.PrincipalPointY);
        _Shader.SetFloat("fx", _CameraIntrinsics.FocalLengthX);
        _Shader.SetFloat("fx", _CameraIntrinsics.FocalLengthY);
        int numThread = _TrianglesTemplate.Length / 3 / 4;
        //_Shader.SetInt("numThread",numThread);

        int kid = _Shader.FindKernel("CSMain");
        _Shader.SetBuffer(kid, "buffer", _Buffer);
        _Shader.Dispatch(kid, numThread, 1, 1);
    }

    private double GetAvg(ushort[] depthData, int x, int y, int width, int height)
    {
        double sum = 0.0;

        for (int y1 = y; y1 < y + 4; y1++)
        {
            for (int x1 = x; x1 < x + 4; x1++)
            {
                int fullIndex = (y1 * width) + x1;

                if (depthData[fullIndex] == 0)
                    sum += 4500;
                else
                    sum += depthData[fullIndex];

            }
        }

        return sum / 16;
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
