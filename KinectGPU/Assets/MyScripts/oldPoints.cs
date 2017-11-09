using UnityEngine;
using System.Collections;
using Windows.Kinect;

public class oldPoints : MonoBehaviour
{

    public GameObject MultiSourceManager;
    public float iZMax = 2.0f;
    public float iZMin = 0.5f;
    public float distanceLimit = 0.5f;
    private KinectSensor _Sensor;
    private CoordinateMapper _Mapper;
    private Mesh _Mesh;
    private Vector3[] _Vertices;
    private Vector2[] _UV;
    private int[] _Triangles;

    // Only works at 4 right now
    private const int _DownsampleSize = 4;
    private const double _DepthScale = 0.1f;
    private const int _Speed = 50;

    private MultiSourceManager _MultiManager;
    private CameraIntrinsics _CameraIntrinsics = new CameraIntrinsics();
    private float[] colmap;
    private float[] rowmap;
    void Start()
    {

        _Sensor = KinectSensor.GetDefault();
        if (_Sensor != null)
        {
            _Mapper = _Sensor.CoordinateMapper;
            _CameraIntrinsics = _Mapper.GetDepthCameraIntrinsics();

            var frameDesc = _Sensor.DepthFrameSource.FrameDescription;

            // Downsample to lower resolution
            CreateMesh(frameDesc.Width / _DownsampleSize, frameDesc.Height / _DownsampleSize);

            if (!_Sensor.IsOpen)
            {
                _Sensor.Open();
            }
        }

        // 初始化参数
        colmap = new float[512];
        rowmap = new float[424];

        for (int i = 0; i < 512; i++)
            colmap[i] = (i - _CameraIntrinsics.PrincipalPointX + 0.5f) / _CameraIntrinsics.FocalLengthX;
        for (int i = 0; i < 424; i++)
            rowmap[i] = (i - _CameraIntrinsics.PrincipalPointY+ 0.5f) / _CameraIntrinsics.FocalLengthY;
    }

    void CreateMesh(int width, int height)
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

    private void RefreshData(ushort[] depthData, int colorWidth, int colorHeight)
    {
        var frameDesc = _Sensor.DepthFrameSource.FrameDescription;

        ColorSpacePoint[] colorSpace = new ColorSpacePoint[depthData.Length];
        _Mapper.MapDepthFrameToColorSpace(depthData, colorSpace);

        for (int y = 0; y < frameDesc.Height; y += _DownsampleSize)
        {
            for (int x = 0; x < frameDesc.Width; x += _DownsampleSize)
            {
                int indexX = x / _DownsampleSize;
                int indexY = y / _DownsampleSize;
                int smallIndex = (indexY * (frameDesc.Width / _DownsampleSize)) + indexX;

                double avg = GetAvg(depthData, x, y, frameDesc.Width, frameDesc.Height);

                avg = avg * _DepthScale;
                int temp = depthData[(y * frameDesc.Width) + x];
                if (temp == 0)
                {
                    continue;
                }
                //_Vertices[smallIndex].z = (float)avg;
                _Vertices[smallIndex].z = (int)depthData[(y * frameDesc.Width) + x] / 1000.0f;
                _Vertices[smallIndex].x = -colmap[x] * _Vertices[smallIndex].z;
                _Vertices[smallIndex].y = -rowmap[y] * _Vertices[smallIndex].z;



                // Update UV mapping with CDRP
                var colorSpacePoint = colorSpace[(y * frameDesc.Width) + x];
                _UV[smallIndex] = new Vector2(colorSpacePoint.X / colorWidth, colorSpacePoint.Y / colorHeight);
            }
        }

        int[] indecies = new int[_Vertices.Length];
        for (int i = 0; i < _Vertices.Length; ++i)
        {
            indecies[i] = i;
        }

        // Build triangle indices: 3 indices into vertex array for each triangle
        // 三角化的序号数组，每个三角形需要三个序号来表示，而每个网格有两个三角形
        int height = 424 / 4;
        int width = 512 / 4;
        

        int[] _triangles = new int[(height - 1) * (width - 1) * 6];

        bool backGroundTriangles = false;
        int index = 0;
        
        for (int y = 0; y < height - 1; y++)
        {
            for (int x = 0; x < width - 1; x++)
            {
                // 不在深度范围（0-MaxDepthVal）的三角就要去除掉
                if (true)
                {
                    backGroundTriangles = (
                        (Mathf.Abs(_Vertices[y * width + x].z) > iZMax) ||
                        (Mathf.Abs(_Vertices[y * width + x + 1].z) > iZMax) ||
                        (Mathf.Abs(_Vertices[(y + 1) * width + x].z) > iZMax) ||
                        (Mathf.Abs(_Vertices[(y + 1) * width + x + 1].z) > iZMax)
                    );

                    backGroundTriangles = backGroundTriangles || (
                        (Mathf.Abs(_Vertices[y * width + x].z) <= iZMin) ||
                        (Mathf.Abs(_Vertices[y * width + x + 1].z) <= iZMin) ||
                        (Mathf.Abs(_Vertices[(y + 1) * width + x].z) <= iZMin) ||
                        (Mathf.Abs(_Vertices[(y + 1) * width + x + 1].z) <= iZMin)
                    );

                    backGroundTriangles = backGroundTriangles || (
                        (Mathf.Abs(_Vertices[y * width + x].z - _Vertices[y * width + x + 1].z) >= distanceLimit) ||
                        (Mathf.Abs(_Vertices[y * width + x].z - _Vertices[(y + 1) * width + x].z) >= distanceLimit) ||
                        (Mathf.Abs(_Vertices[(y + 1) * width + x].z - _Vertices[(y + 1) * width + x + 1].z) >= distanceLimit) ||
                        (Mathf.Abs(_Vertices[(y) * width + x + 1].z - _Vertices[(y + 1) * width + x + 1].z) >= distanceLimit) 

                    );
                }
                if (!backGroundTriangles)
                {
                    // For each grid cell output two triangles	
                    // 左下的三角
                    _triangles[index++] = (y * width) + x;
                    _triangles[index++] = ((y + 1) * width) + x;
                    _triangles[index++] = (y * width) + x + 1;
                    // 右上的三角
                    _triangles[index++] = ((y + 1) * width) + x;
                    _triangles[index++] = ((y + 1) * width) + x + 1;
                    _triangles[index++] = (y * width) + x + 1;
                }


            }
        }
        //Debug.LogError(index);
        for (; index < (height - 1) * (width - 1) * 6; index++)
        {
            _triangles[index] = 0;
        }


        _Mesh.vertices = _Vertices;
        _Mesh.uv = _UV;
        _Mesh.triangles = _triangles;
        //_Mesh.SetIndices(indecies, MeshTopology.Points, 0);
        //_Mesh.RecalculateNormals();
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
