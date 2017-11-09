using UnityEngine;
using System.Collections;
using Windows.Kinect;
using UnityEngine.UI;
using System.Text;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.IO;
public class MultiSourceManager : MonoBehaviour
{
    public int ColorWidth { get; private set; }
    public int ColorHeight { get; private set; }

    public ushort iDeadLine = 0x0200;

    private KinectSensor _Sensor = null;
    //private CoordinateMapper _Mapper;       // jiao--坐标转换器
    private MultiSourceFrameReader _Reader;
    private Texture2D _ColorTexture;
    private Texture2D textureTemp;
    private ushort[] _DepthData;
    private byte[] _ColorData;
    private byte[] _BodyIndexData;

    /// <summary>
    /// jiao--表示kinect是否已经启动
    /// </summary>
    private bool bIsOpen = false;

    public Texture2D GetColorTexture()
    {
        return _ColorTexture;
    }

    public ushort[] GetDepthData()
    {
        return _DepthData;
    }

    public byte[] GetDepthData_high()
    {
        return CovertUshortsTo8bits(_DepthData, _BodyIndexData, 512, 424, 512, 424, 1, iDeadLine);
    }
    /// <summary>
    /// 获得320*240*8bits的深度信息
    /// </summary>
    /// <returns></returns>
    public byte[] GetDepthData320_240()
    {
        byte[] bsTemp = CovertUshortsTo8bits(_DepthData, _BodyIndexData, 512, 424, 320, 240, 1, iDeadLine);
        //IntPtr y;//初始化 略
        //byte[] bsFilter = new byte[bsTemp.Length];
        //string strIn = System.Text.Encoding.ASCII.GetString(bsTemp);
        //string strOut = System.Text.Encoding.ASCII.GetString(bsFilter);
        ////y = BilateralFilter(strIn, strOut, 320, 240, 5);
        //y = GaussianFilter(strIn, strOut, 320, 240, 3, 3, 0, 0,4);
        //Marshal.Copy(y, bsFilter, 0, bsTemp.Length);
        // 8bits
        return Smoothing(bsTemp, 320, 240);
        //return bsTemp;
    }

    /// <summary>
    /// 缩放纹理图
    /// </summary>
    /// <param name="source"></param>
    /// <param name="targetWidth"></param>
    /// <param name="targetHeight"></param>
    /// <returns></returns>
    Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
    {
        //Texture2D result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
        textureTemp.Resize(targetWidth, targetHeight);
        for (int i = 0; i < textureTemp.height; ++i)
        {
            for (int j = 0; j < textureTemp.width; ++j)
            {
                Color newColor = source.GetPixel((int)((float)j / (float)textureTemp.width*source.width), (int)((float)i / (float)textureTemp.height*source.height));
                //Color newColor = source.GetPixelBilinear((float)j / (float)textureTemp.width, (float)i / (float)textureTemp.height);
                textureTemp.SetPixel(j, i, newColor);
            }
        }
        textureTemp.Apply();
        //Debug.LogError(result.GetRawTextureData().Length);
        return textureTemp;
    }
    public byte[] GetColorDataScaled()
    {
        return ScaleTexture(_ColorTexture, 512, 424).GetRawTextureData();
    }
    public byte[] GetColorDataLowScaled()
    {
        byte[] bsTemp = ScaleTexture(_ColorTexture, 320, 240).GetRawTextureData();
        //IntPtr y;//初始化 略
        //byte[] bsFilter = new byte[bsTemp.Length];
        //string strIn = System.Text.Encoding.ASCII.GetString(bsTemp);
        //string strOut = System.Text.Encoding.ASCII.GetString(bsFilter);
        ////y = BilateralFilter(strIn, strOut, 320, 240, 5);
        //y = GaussianFilter(strIn, strOut, 320, 240, 3, 3, 0, 0, 2);
        //Marshal.Copy(y, bsFilter, 0, bsTemp.Length);
        return bsTemp;
    }
    void Start()
    {
        Openkinect();
    }

    void Update()
    {
        if (_Reader != null)
        {
            var frame = _Reader.AcquireLatestFrame();
            if (frame != null)
            {
                var colorFrame = frame.ColorFrameReference.AcquireFrame();
                if (colorFrame != null)
                {
                    var depthFrame = frame.DepthFrameReference.AcquireFrame();
                    var bodyIndexFrame = frame.BodyIndexFrameReference.AcquireFrame();
                    if (depthFrame != null)
                    {
                        colorFrame.CopyConvertedFrameDataToArray(_ColorData, ColorImageFormat.Rgba);
                        _ColorTexture.LoadRawTextureData(_ColorData);
                        _ColorTexture.Apply();

                        //TextureScale.Bilinear(_ColorTexture, 512, 424);

                        depthFrame.CopyFrameDataToArray(_DepthData);
                        bodyIndexFrame.CopyFrameDataToArray(_BodyIndexData);

                        depthFrame.Dispose();
                        depthFrame = null;
                    }
                    colorFrame.Dispose();
                    colorFrame = null;
                }

                frame = null;
            }
        }
    }

    void OnApplicationQuit()
    {
        // 关闭kinect
        if (bIsOpen)
            Closekinect();

        if (_Reader != null)
        {
            _Reader.Dispose();
            _Reader = null;
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

    // jiao--打开Kinect
    public void Openkinect()
    {
        if (_Sensor != null)
            return;

        _Sensor = KinectSensor.GetDefault();

        if (_Sensor == null)
            Debug.LogError("Can't find kinect -- jiao");

        if (_Sensor != null)
        {
            _Reader = _Sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth );//| FrameSourceTypes.BodyIndex);

            var colorFrameDesc = _Sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Rgba);
            ColorWidth = colorFrameDesc.Width;
            ColorHeight = colorFrameDesc.Height;

            textureTemp = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            _ColorTexture = new Texture2D(colorFrameDesc.Width, colorFrameDesc.Height, TextureFormat.RGBA32, false);
            _ColorData = new byte[colorFrameDesc.BytesPerPixel * colorFrameDesc.LengthInPixels];

            var depthFrameDesc = _Sensor.DepthFrameSource.FrameDescription;
            _DepthData = new ushort[depthFrameDesc.LengthInPixels];
            _BodyIndexData = new byte[depthFrameDesc.LengthInPixels];

            if (!_Sensor.IsOpen)
            {
                _Sensor.Open();
            }
        }
    }
    // jiao--关闭kinect
    public void Closekinect()
    {
        // 表示关闭kinect
        if (_Reader != null)
        {
            _Reader.Dispose();
            _Reader = null;
        }

        if (_Sensor != null)
        {
            if (_Sensor.IsOpen)
            {
                _Sensor.Close();
            }

            _Sensor = null;
        }
        bIsOpen = false;
    }

    // jiao--判定kinect状态
    public bool IsOpen()
    {
        return bIsOpen;
    }

    /// <summary>
    /// jiao--把512*424的ushort数组转变成为320*204*1的数组
    /// </summary>
    /// <param name="usDepthData"></param>
    /// <param name="iSrcW"></param>
    /// <param name="iSrcH"></param>
    /// <param name="iDesW"></param>
    /// <param name="iDesH"></param>
    /// <param name="bits"></param>
    /// <returns></returns>
    public byte[] CovertUshortsTo8bits(ushort[] usDepthData, byte[] bsBodyIndexData, int iSrcW, int iSrcH, int iDesW, int iDesH, int bits, ushort iDeadLine)
    {
        byte[] bsResult = new byte[iDesW * iDesH * bits];
        for (int i = 0; i < iDesH; i++)
        {
            int iHOffset = (i + (iSrcH - iDesH) / 2) * iSrcW * bits;
            int iWOffset = (iSrcW - iDesW) / 2 * bits;
            for (int j = 0; j < iDesW; j++)
            {
                //// 若没追踪到人物
                //if (bsBodyIndexData[iHOffset + iWOffset + j] == 0xff)
                //{
                //    bsResult[i * iDesW * bits + j] = 0x00;
                //    continue;
                //}
                ushort temp = usDepthData[iHOffset + iWOffset + j];
                // 超出范围
                if (temp < iDeadLine || temp >= (iDeadLine + 0x0200))
                {
                    bsResult[i * iDesW * bits + j] = 0x00;
                }
                else
                {
                    // 不确定是0还是1
                    bsResult[i * iDesW * bits + j] = (byte)((temp - iDeadLine) >> 1);
                }
            }
        }
        return bsResult;
    }

    private byte[] Smoothing(byte[] dataIn, int width,int height)
    {
        byte[] dataOut = new byte[dataIn.Length];
        // 我们用这两个值来确定索引在正确的范围内
        int widthBound = width - 1;
        int heightBound = height - 1;

        // 内（8个像素）外（16个像素）层阈值
        int innerBandThreshold = 3;
        int outerBandThreshold = 7;

        // 处理每行像素
        for (int depthArrayRowIndex = 0; depthArrayRowIndex < height; depthArrayRowIndex++)
        {
            // 处理一行像素中的每个像素
            for (int depthArrayColumnIndex = 0; depthArrayColumnIndex < width; depthArrayColumnIndex++)
            {
                int depthIndex = depthArrayColumnIndex + (depthArrayRowIndex * width);

                // 我们认为深度值为0的像素即为候选像素
                if (dataIn[depthIndex] == 0)
                {
                    // 通过像素索引，我们可以计算得到像素的横纵坐标（x,y）
                    int x = depthIndex % width;
                    int y = (depthIndex - x) / width;

                    // filter collection 用来计算滤波器内每个深度值对应的频度，在后面
                    // 我们将通过这个数值来确定给候选像素一个什么深度值。
                    byte[,] filterCollection = new byte[24, 2];

                    // 内外层框内非零像素数量计数器，在后面用来确定候选像素是否滤波
                    int innerBandCount = 0;
                    int outerBandCount = 0;

                    // 下面的循环将会对以候选像素为中心的5 X 5的像素阵列进行遍历。这里定义了两个边界。如果在
                    // 这个阵列内的像素为非零，那么我们将记录这个深度值，并将其所在边界的计数器加一，如果计数器
                    // 高过设定的阈值，那么我们将取滤波器内统计的深度值的众数（频度最高的那个深度值）应用于候选
                    // 像素上
                    for (int yi = -2; yi < 3; yi++)
                    {
                        for (int xi = -2; xi < 3; xi++)
                        {
                            // yi和xi为操作像素相对于候选像素的平移量

                            // 我们不要xi = 0&&yi = 0的情况，因为此时操作的就是候选像素
                            if (xi != 0 || yi != 0)
                            {
                                // 确定操作像素在深度图中的位置
                                int xSearch = x + xi;
                                int ySearch = y + yi;

                                // 检查操作像素的位置是否超过了图像的边界（候选像素在图像的边缘）
                                if (xSearch >= 0 && xSearch <= widthBound &&
                                    ySearch >= 0 && ySearch <= heightBound)
                                {
                                    int index = xSearch + (ySearch * width);
                                    // 我们只要非零量
                                    if (dataIn[index] != 0)
                                    {
                                        // 计算每个深度值的频度
                                        for (int i = 0; i < 24; i++)
                                        {
                                            if (filterCollection[i, 0] == dataIn[index])
                                            {
                                                // 如果在 filter collection中已经记录过了这个深度
                                                // 将这个深度对应的频度加一
                                                filterCollection[i, 1]++;
                                                break;
                                            }
                                            else if (filterCollection[i, 0] == 0)
                                            {
                                                // 如果filter collection中没有记录这个深度
                                                // 那么记录
                                                filterCollection[i, 0] = dataIn[index];
                                                filterCollection[i, 1]++;
                                                break;
                                            }
                                        }

                                        // 确定是内外哪个边界内的像素不为零，对相应计数器加一
                                        if (yi != 2 && yi != -2 && xi != 2 && xi != -2)
                                            innerBandCount++;
                                        else
                                            outerBandCount++;
                                    }
                                }
                            }
                        }
                    }

                    // 判断计数器是否超过阈值，如果任意层内非零像素的数目超过了阈值，
                    // 就要将所有非零像素深度值对应的统计众数
                    if (innerBandCount >= innerBandThreshold || outerBandCount >= outerBandThreshold)
                    {
                        ushort frequency = 0;
                        byte depth = 0;
                        // 这个循环将统计所有非零像素深度值对应的众数
                        for (int i = 0; i < 24; i++)
                        {
                            // 当没有记录深度值时（无非零深度值的像素）
                            if (filterCollection[i, 0] == 0)
                                break;
                            if (filterCollection[i, 1] > frequency)
                            {
                                depth = filterCollection[i, 0];
                                frequency = filterCollection[i, 1];
                            }
                        }

                        dataOut[depthIndex] = depth;
                    }
                    else
                    {
                        dataOut[depthIndex] = 0;
                    }
                }
                else
                {
                    // 如果像素的深度值不为零，保持原深度值
                    dataOut[depthIndex] = dataIn[depthIndex];
                }
            }
        }
        return dataOut;
    }

    [DllImport(@"DepthFilterDll", EntryPoint = "BilateralFilter", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    extern static IntPtr BilateralFilter(string ImageIn, string ImageOut, int width, int height, int value, int option);
    [DllImport(@"DepthFilterDll", EntryPoint = "GaussianFilter", CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    extern static IntPtr GaussianFilter(string ImageIn, string ImageOut, int width, int height, int sizeWidth, int sizeHeight, int sigmaX, int sigmaY, int option);
}
