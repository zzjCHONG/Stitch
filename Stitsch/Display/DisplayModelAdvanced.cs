using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System.Diagnostics;
using System.Windows.Media.Imaging;

namespace Simscop.Pl.Core.Models.Advanced
{
    //输入8UC3，针对的是merge图像，即进行亮度对比度调整再转frame，无需归一化、伽玛与伪彩处理
    //输入任意的C1图像，即进行归一化-亮度对比度伽玛-伪彩-转frame处理

    public partial class DisplayModel : ObservableObject
    {
        //ORIGIN -(norm)-> U8 -(gamma/brightness/contrast)-> Display -(applycolor/convertertosource)-> Frame

        [ObservableProperty]
        private Mat _original = new();

        [ObservableProperty]
        private Mat _u8 = new();//original-changed,apply norm

        [ObservableProperty]
        private Mat _display = new();//U8-changed，apply _brightness，contrast,gamma

        [ObservableProperty]
        private BitmapFrame? _frame;//display基础上添加colorMode，并转换类型

        [ObservableProperty]
        private double _contrast = 1;

        [ObservableProperty]
        private int _brightness = 0;

        [ObservableProperty]
        private double _gamma = 1;

        [ObservableProperty]
        private bool _norm = true;

        [ObservableProperty]
        private int _min = 0;

        [ObservableProperty]
        private int _max = 255;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ContrastThreshold))]
        private int _threshold = 255;

        public int ContrastThreshold => Threshold / 2;

        [ObservableProperty]
        private int _colorMode = 0;

        [ObservableProperty]
        private int _normIndex = 1;

        [ObservableProperty]
        private List<string> _normMode = new()
        {
            "None",
            "DynamicMinMax" ,
            "MaxOnly",
            "FixedRange",
            "Percentile",
            "ZScore" ,
            "Gamma",
            "LogScale",
            "Translation+DynamicMinMax"
        };

        /// <summary>
        /// 百分位裁剪时使用
        /// </summary>
        [ObservableProperty]
        private int _percenttileScore=5;

        void Init()
        {
            // 修改点：区分8UC3和其他类型
            if (Original.Type() == MatType.CV_8UC3)
            {
                // 8UC3直接赋值，不归一化
                U8 = Original.Clone();
            }
            else if (Norm)
            {
                Min = 0;
                Max = Threshold;
                Threshold = 255;
                U8 = Original.To8UC1(NormIndex, PercenttileScore);
            }
            else
            {
                ResetU8();
            }
        }

        void ResetU8()
        {
            if (!ValidMat(Original)) return;

            // 修改点：区分8UC3和其他类型
            if (Original.Type() == MatType.CV_8UC3)
            {
                U8 = Original.Clone();
                return;
            }

            //0-CV_8U,1-CV_8S,2-CV_16U,3-CV_16S,4-CV_32S,5-CV_32F,6-CV_64F
            var depth = Original.Depth();
            switch (depth)
            {
                case 0:
                    Threshold = 255;
                    break;
                case 2:
                    Threshold = 65535;
                    break;
                default:
                    Debug.WriteLine($"Original-{Original.Type()};默认使用归一化处理！");
                    Norm = true;
                    return;
            }

            var temp = Original.RangeIn(Min, Max);
            Cv2.Subtract(temp, new Scalar(Min), temp);
            Cv2.Divide(temp, new Scalar(Max - Min), temp);
            Cv2.Multiply(temp, new Scalar(255), temp);

            U8 = temp.To8UC1();
        }

        bool ValidMat(Mat mat)
        => mat.Cols != 0 || mat.Rows != 0;

        void TempDisplayDo()
        {
            if (!ValidMat(U8)) return;

            // 修改点：支持8UC3的Display调整
            if (U8.Type() == MatType.CV_8UC1)
            {
                Display = U8.Gamma(Gamma).Adjust(Contrast, Brightness);
            }
            else if (U8.Type() == MatType.CV_8UC3)
            {
                // 8UC3不做Gamma，仅做亮度对比度
                Display = U8.Adjust(Contrast, Brightness);
            }
            else
            {
                // 其他类型暂不处理
                Display = U8.Clone();
            }
        }

        void TempFrameDo()
        {
            if (!ValidMat(Display)) return;

            // 修改点：8UC1才加伪彩，8UC3直接显示
            if (Display.Type() == MatType.CV_8UC1)
            {
                Frame = BitmapFrame.Create(Display.ApplyColor(ColorMode).ToBitmapSource());
            }
            else if (Display.Type() == MatType.CV_8UC3)
            {
                // 8UC3直接转BitmapSource
                Frame = BitmapFrame.Create(Display.ToBitmapSource());
            }
            else
            {
                Frame = null;
            }
        }

        partial void OnOriginalChanged(Mat value) => Init();

        partial void OnNormChanged(bool value) => Init();

        partial void OnNormIndexChanged(int value) => Init();

        partial void OnU8Changed(Mat value)
            => Display = U8.Gamma(Gamma).Adjust(Contrast, Brightness);

        partial void OnDisplayChanged(Mat value)
            => TempFrameDo();

        partial void OnColorModeChanged(int value)
            => TempFrameDo();

        partial void OnMinChanged(int value)
        {
            if (Norm) return;
            ResetU8();
        }

        partial void OnMaxChanged(int value)
        {
            if (Norm) return;
            ResetU8();
        }

        partial void OnContrastChanged(double value)
            => TempDisplayDo();

        partial void OnBrightnessChanged(int value)
            => TempDisplayDo();

        partial void OnGammaChanged(double value)
            => TempDisplayDo();

        [RelayCommand]
        void SetAsDefault()
        {
            Norm = true;
            Contrast = 1;
            Brightness = 0;
            Gamma = 1;
            ColorMode = 0;

            NormIndex = 1;
        }

    }

    public static class DisplayModelExtension
    {
        /// <summary>
        /// 转换任意xC1数据类型成为64FC1
        /// </summary>c
        /// <param name="mat"></param>
        /// <returns></returns>
        public static Mat? To64F(this Mat mat)
        {
            if (mat.Channels() != 1) return null;
            var temp = new Mat(mat.Rows, mat.Cols, MatType.CV_64FC1);
            mat.ConvertTo(temp, MatType.CV_64FC1, 1, 0);
            return temp;
        }

        /// <summary>
        /// 范围外值为0
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static Mat RangeIn(this Mat mat, double min, double max)
        {
            var mask = new Mat();
            Cv2.InRange(mat, new Scalar(min, min, min), new Scalar(max, max, max), mask);

            var result = new Mat();
            Cv2.BitwiseAnd(mat, mat, result, mask);

            return result;
        }

        /// <summary>   
        /// 原始数据
        /// 这里处理也是只针对单通道的数据
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="mode">
        /// 转换方式：
        /// 0 - 不做额外处理，使用标准的转换
        /// 1 - 最大最小归一化
        /// 2 - 等比例放缩
        /// .....
        /// </param>
        /// <returns></returns>
        public static Mat To8UC1(this Mat mat, int mode = 0, int percentile = 5)
        {
            var output = new Mat();

            if (mat == null || mat.Empty()) return output;

            if (mat.Channels() > 1)
                mat = mat.CvtColor(ColorConversionCodes.BGR2GRAY);

            if (mat.Type() == MatType.CV_8UC1)
                return mat;

            if (mat.Type() == MatType.CV_16UC1)
            {
                mat.ConvertTo(output, MatType.CV_8UC1, 1.0 / 257);
                return output;
            }

            switch (mode)
            {
                case 0://不做归一化，直接强制类型转换。此方式适合原始值本就在[0, 255] 之间的图像
                    mat.ConvertTo(output, MatType.CV_8UC1, 1, 0);
                    return output;

                case 1://动态 min-max 归一化
                    var mat1 = mat.To64F();
                    mat1!.MinMaxLoc(out var min, out double max);

                    if (Math.Abs(max - min) < 1e-6)//防止除以 0
                        return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                    var temp1 = new Mat();
                    Cv2.Subtract(mat1, new Scalar(min), temp1); // temp1 = mat1 - min
                    Cv2.Divide(temp1, new Scalar(max - min), temp1); // temp1 = temp1 / (max - min)
                    Cv2.Multiply(temp1, new Scalar(255), temp1); // temp1 = temp1 * 255
                    temp1.ConvertTo(output, MatType.CV_8UC1);

                    return output;

                case 2://最大值归一化
                    Mat mat2 = mat.Type() == MatType.CV_32FC1 ? mat.Clone() : new Mat();
                    if (mat.Type() != MatType.CV_32FC1) mat.ConvertTo(mat2, MatType.CV_32FC1);

                    // 获取最大值（最小值不参与）
                    mat2.MinMaxLoc(out _, out double maxVal);

                    // 防止除以 0（图像为全黑）
                    if (Math.Abs(maxVal) < 1e-6)
                        return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                    // 可选：将所有小于 0 的像素裁剪为 0
                    // 若图像本应为非负实数图（如光强图），推荐启用此裁剪
                    // Cv2.Max(mat32, new Scalar(0), mat32);

                    // 归一化：值除以最大值并乘以 255
                    var temp2 = new Mat();
                    Cv2.Multiply(mat2, new Scalar(255.0 / maxVal), temp2);

                    // 转换为 8 位图像
                    temp2.ConvertTo(output, MatType.CV_8UC1);
                    return output;

                case 3://固定范围归一化
                    const double fixedMin = 100.0;
                    const double fixedMax = 10000.0;

                    if (Math.Abs(fixedMax - fixedMin) < 1e-6)
                        return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                    // 确保浮点类型
                    var mat3 = mat.Type() == MatType.CV_32FC1 ? mat.Clone() : new Mat();
                    if (mat.Type() != MatType.CV_32FC1)
                        mat.ConvertTo(mat3, MatType.CV_32FC1);

                    var temp3 = new Mat();

                    // 归一化： (x - min) / (max - min)
                    Cv2.Subtract(mat3, new Scalar(fixedMin), temp3);
                    Cv2.Divide(temp3, new Scalar(fixedMax - fixedMin), temp3);

                    // 裁剪到 [0,1]
                    Cv2.Min(temp3, new Scalar(1.0), temp3);
                    Cv2.Max(temp3, new Scalar(0.0), temp3);

                    // 放缩到 0~255
                    Cv2.Multiply(temp3, new Scalar(255), temp3);
                    temp3.ConvertTo(output, MatType.CV_8UC1);

                    return output;

                case 4: // 百分位裁剪归一化（Percentile Normalization）
                    {
                        // 转为 float 类型以计算百分位
                        var mat4 = mat.Type() == MatType.CV_32FC1 ? mat.Clone() : new Mat();
                        if (mat.Type() != MatType.CV_32FC1)
                            mat.ConvertTo(mat4, MatType.CV_32FC1);

                        // 拉平为 1D 数组
                        //var data = new float[mat4.Rows * mat4.Cols];
                        //mat4.GetArray(0, 0, data);
                        //unsafe
                        //{
                        //    float* srcPtr = (float*)mat4.Data.ToPointer();
                        //    for (int i = 0; i < data.Length; i++)
                        //    {
                        //        data[i] = srcPtr[i];
                        //    }
                        //}
                        float[] data = new float[mat4.Rows * mat4.Cols];
                        var indexer = mat4.GetGenericIndexer<float>();
                        int k = 0;
                        for (int i = 0; i < mat4.Rows; i++)
                            for (int j = 0; j < mat4.Cols; j++)
                                data[k++] = indexer[i, j];

                        // 排序计算百分位
                        Array.Sort(data);
                        int total = data.Length;
                        float pLow = data[(int)(total * percentile * 0.01)];
                        float pHigh = data[(int)(total * ((100 - percentile )* 0.01))];

                        if (Math.Abs(pHigh - pLow) < 1e-6)
                            return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                        // 开始归一化
                        var temp4 = new Mat();
                        Cv2.Subtract(mat4, new Scalar(pLow), temp4);
                        Cv2.Divide(temp4, new Scalar(pHigh - pLow), temp4);
                        Cv2.Min(temp4, new Scalar(1.0), temp4);
                        Cv2.Max(temp4, new Scalar(0.0), temp4);
                        Cv2.Multiply(temp4, new Scalar(255), temp4);
                        temp4.ConvertTo(output, MatType.CV_8UC1);

                        return output;
                    }

                case 5: // Z-score 标准化后归一化
                    {
                        var mat64 = mat.To64F();

                        // 计算均值和标准差
                        var meanMat = new Mat();
                        var stddevMat = new Mat();
                        mat64!.MeanStdDev(meanMat, stddevMat);

                        double mean = meanMat.Get<double>(0);
                        double stddev = stddevMat.Get<double>(0);

                        if (stddev < 1e-6)
                            return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                        // Z-score 标准化
                        var temp = new Mat();
                        Cv2.Subtract(mat64, new Scalar(mean), temp);
                        Cv2.Divide(temp, new Scalar(stddev), temp);

                        // 限制范围在 [-3, 3]
                        Cv2.Min(temp, new Scalar(3), temp);
                        Cv2.Max(temp, new Scalar(-3), temp);

                        // 映射到 [0,255]
                        Cv2.Add(temp, new Scalar(3), temp);       // 平移到 [0,6]
                        Cv2.Divide(temp, new Scalar(6), temp);    // 归一化到 [0,1]
                        Cv2.Multiply(temp, new Scalar(255), temp);

                        temp.ConvertTo(output, MatType.CV_8UC1);
                        return output;
                    }

                case 6: // Gamma 归一化
                    {
                        const double gamma = 0.5; // 你可以改成参数输入，gamma < 1 增亮， > 1 变暗

                        var mat6 = mat.To64F();
                        mat6!.MinMaxLoc(out double min6, out double max6);

                        if (Math.Abs(max6 - min6) < 1e-6)
                            return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                        var temp = new Mat();
                        Cv2.Subtract(mat6, new Scalar(min6), temp);
                        Cv2.Divide(temp, new Scalar(max6 - min6), temp);

                        // Gamma 校正 (x ^ gamma)
                        // 这里对每个像素做幂运算
                        unsafe
                        {
                            var indexer = temp.GetGenericIndexer<double>();
                            for (int i = 0; i < temp.Rows; i++)
                            {
                                for (int j = 0; j < temp.Cols; j++)
                                {
                                    indexer[i, j] = Math.Pow(indexer[i, j], gamma);
                                }
                            }
                        }

                        Cv2.Multiply(temp, new Scalar(255), temp);
                        temp.ConvertTo(output, MatType.CV_8UC1);
                        return output;
                    }

                case 7: // Log 归一化
                    {
                        var mat7 = mat.To64F();
                        var temp7 = new Mat();

                        // 加 1 防止 log(0)
                        Cv2.Add(mat7!, new Scalar(1), temp7);

                        // 计算 log(x)
                        unsafe
                        {
                            var indexer = temp7.GetGenericIndexer<double>();
                            for (int i = 0; i < temp7.Rows; i++)
                            {
                                for (int j = 0; j < temp7.Cols; j++)
                                {
                                    indexer[i, j] = Math.Log(indexer[i, j]);
                                }
                            }
                        }

                        temp7.MinMaxLoc(out double min7, out double max7);

                        if (Math.Abs(max7 - min7) < 1e-6)
                            return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                        // 归一化到 [0, 255]
                        Cv2.Subtract(temp7, new Scalar(min7), temp7);
                        Cv2.Divide(temp7, new Scalar(max7 - min7), temp7);
                        Cv2.Multiply(temp7, new Scalar(255), temp7);

                        temp7.ConvertTo(output, MatType.CV_8UC1);
                        return output;
                    }
                case 8: // 平移+动态归一化
                    {
                        var mat8 = mat.To64F();
                        mat8!.MinMaxLoc(out double min8, out double max8);

                        if (Math.Abs(max8 - min8) < 1e-6)
                            return new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(0));

                        var temp8 = new Mat();
                        Cv2.Subtract(mat8, new Scalar(min8), temp8);
                        Cv2.Divide(temp8, new Scalar(max8 - min8), temp8);
                        Cv2.Multiply(temp8, new Scalar(255), temp8);
                        temp8.ConvertTo(output, MatType.CV_8UC1);
                        return output;
                    }

                default:
                    break;
            }

            return output;
        }

        /// <summary>
        /// 将一系列的8UC1数据格式计算平均值
        /// </summary>
        /// <param name="mats"></param>
        /// <returns></returns>
        public static Mat? Average(this List<Mat> mats)
        {
            if (!mats.All(item => item.Depth() == 0 && item.Channels() == 1))
                return null;

            if (mats.Count < 2)
                return mats.Count == 1 ? mats[0] : null;

            var average = new Mat(mats[0].Rows, mats[0].Cols, MatType.CV_64FC1, new Scalar(0));
            average = mats.Aggregate(average, (current, mat) => (Mat)(current + mat.To64F()!));

            average.GetArray(out double[] data);

            average /= mats.Count;

            var u8 = new Mat();
            average.ConvertTo(u8, MatType.CV_8UC1);
            return u8;
        }

        /// <summary>
        /// 两个图像逐像素最大值的合成图
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="match"></param>
        /// <returns></returns>
        public static Mat Max(this Mat mat, Mat match)
        {
            var temp = new Mat();
            Cv2.Max(mat, match, temp);
            return temp;
        }

        /// <summary>
        /// 输入多个单通道灰度图像,返回一个图像，其中每个像素是多个图像中该位置的最大像素值
        /// </summary>
        /// <param name="mats"></param>
        /// <returns></returns>
        public static Mat? Max(this List<Mat> mats)
        {
            if (!mats.All(item => item.Depth() == 0 && item.Channels() == 1))
                return null;

            if (mats.Count < 2)
                return mats.Count == 1 ? mats[0] : null;

            var max = new Mat(mats[0].Rows, mats[0].Cols, MatType.CV_8UC1, new Scalar(0));
            max = mats.Aggregate(max, (current, mat) => (Mat)(current.Max(mat)));
            return max;
        }

        /// <summary>
        /// 伪彩设置
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="color"></param>
        /// <returns></returns>
        public static Mat Apply(this Mat mat, Mat color)
        {
            var res = new Mat();
            Cv2.ApplyColorMap(mat, res, color);
            return res;
        }

        /// <summary>
        /// 伪彩设置
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static Mat Apply(this Mat mat, ColormapTypes type)
        {
            var dst = new Mat();
            Cv2.ApplyColorMap(mat, dst, type);
            return dst;
        }

        /// <summary>
        /// 调整图像的亮度和对比度
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="contrast"></param>
        /// <param name="brightness"></param>
        /// <returns></returns>
        public static Mat Adjust(this Mat mat, double contrast, int brightness)
        {
            if (Math.Abs(contrast - 1) < 0.00001 && brightness == 0) return mat;
            var result = new Mat();
            mat.ConvertTo(result, -1, contrast, brightness);
            return result;
        }

        /// <summary>
        /// 设置图像的gamma值
        /// </summary>
        /// <param name="img"></param>
        /// <param name="gamma"></param>
        /// <returns></returns>
        public static Mat Gamma(this Mat img, double gamma)
        {
            if (img.Type() != MatType.CV_8UC1) return img;

            if (Math.Abs(gamma - 1) < 0.00001) return img;

            var lut = new Mat(1, 256, MatType.CV_8U);
            for (var i = 0; i < 256; i++)
                lut.Set<byte>(0, i, (byte)(Math.Pow(i / 255.0, gamma) * 255.0));

            var output = new Mat();
            Cv2.LUT(img, lut, output);

            return output;
        }

        /// <summary>
        /// 多个三通道图片合并
        /// </summary>
        /// <param name="mats"></param>
        /// <returns></returns>
        public static Mat? MergeChannel3(this List<Mat> mats)
        {
            if (mats.Any(item => item.Channels() != 3))
                return null;

            var ch1 = new List<Mat>();
            var ch2 = new List<Mat>();
            var ch3 = new List<Mat>();

            foreach (var channels in mats.Select(mat => mat.Split()))
            {
                ch1.Add(channels[0]);
                ch2.Add(channels[1]);
                ch3.Add(channels[2]);
            }

            var averCh1 = ch1.Max();
            var averCh2 = ch2.Max();
            var averCh3 = ch3.Max();

            var res = new Mat();
            Cv2.Merge(new Mat[] { averCh1!, averCh2!, averCh3! }, res);

            return res;
        }

        /// <summary>
        /// 伪彩
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        public static Mat ApplyColor(this Mat mat, int mode)
            => mode switch
            {
                0 => mat,
                1 => mat. Apply(ColorMaps.Green),
                2 => mat.Apply(ColorMaps.Red),
                3 => mat.Apply(ColorMaps.Blue),
                4 => mat.Apply(ColorMaps.Pruple),
                5 => mat.Apply(ColormapTypes.Autumn),
                6 => mat.Apply(ColormapTypes.Bone),
                7 => mat.Apply(ColormapTypes.Jet),
                8 => mat.Apply(ColormapTypes.Winter),
                9 => mat.Apply(ColormapTypes.Rainbow),
                10 => mat.Apply(ColormapTypes.Ocean),
                11 => mat.Apply(ColormapTypes.Summer),
                12 => mat.Apply(ColormapTypes.Spring),
                13 => mat.Apply(ColormapTypes.Cool),
                14 => mat.Apply(ColormapTypes.Hsv),
                15 => mat.Apply(ColormapTypes.Pink),
                16 => mat.Apply(ColormapTypes.Hot),
                17 => mat.Apply(ColormapTypes.Parula),
                18 => mat.Apply(ColormapTypes.Magma),
                19 => mat.Apply(ColormapTypes.Inferno),
                20 => mat.Apply(ColormapTypes.Plasma),
                21 => mat.Apply(ColormapTypes.Viridis),
                22 => mat.Apply(ColormapTypes.Cividis),
                23 => mat.Apply(ColormapTypes.Twilight),
                24 => mat.Apply(ColormapTypes.TwilightShifted),
                _ => throw new Exception()
            };

        /// <summary>
        /// 伪彩颜色列举
        /// </summary>
        public static List<string> Colors { get; set; } = new()
        {
            "Gray","Green","Red","Blue","Purple","Autumn",
            "Bone","Jet","Winter","Rainbow","Ocean","Summer",
            "Spring","Cool","Hsv","Pink","Hot","Parula","Magma",
            "Inferno","Plasma","Viridis","Cividis","Twilight",
        };
    }

    public static class ColorMaps
    {
        private static Mat? _gray;

        private static Mat? _green;

        private static Mat? _red;

        private static Mat? _blue;

        private static Mat? _purple;

        public static Mat Gray
        {
            get
            {
                if (_gray != null)
                {
                    return _gray;
                }

                _gray = new Mat(256, 1, MatType.CV_8UC3);
                for (int i = 0; i < 256; i++)
                {
                    _gray.Set(i, 0, new Vec3b((byte)i, (byte)i, (byte)i));
                }

                return _gray;
            }
        }

        public static Mat Green
        {
            get
            {
                if (_green != null)
                {
                    return _green;
                }

                _green = new Mat(256, 1, MatType.CV_8UC3);
                for (int i = 0; i < 256; i++)
                {
                    _green.Set(i, 0, new Vec3b(0, (byte)i, 0));
                }

                return _green;
            }
        }

        public static Mat Red
        {
            get
            {
                if (_red != null)
                {
                    return _red;
                }

                _red = new Mat(256, 1, MatType.CV_8UC3);
                for (int i = 0; i < 256; i++)
                {
                    _red.Set(i, 0, new Vec3b(0, 0, (byte)i));
                }

                return _red;
            }
        }

        public static Mat Blue
        {
            get
            {
                if (_blue != null)
                {
                    return _blue;
                }

                _blue = new Mat(256, 1, MatType.CV_8UC3);
                for (int i = 0; i < 256; i++)
                {
                    _blue.Set(i, 0, new Vec3b((byte)i, 0, 0));
                }

                return _blue;
            }
        }

        public static Mat Pruple
        {
            get
            {
                if (_purple != null)
                {
                    return _purple;
                }

                _purple = new Mat(256, 1, MatType.CV_8UC3);
                for (int i = 0; i < 256; i++)
                {
                    _purple.Set(i, 0, new Vec3b((byte)i, 0, (byte)i));
                }

                return _purple;
            }
        }
    }
}
