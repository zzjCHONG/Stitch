using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using OpenCvSharp;
using Simscop.Pl.Core.Models;
using Simscop.Pl.Core.Models.Advanced;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Rect = OpenCvSharp.Rect;
using Size = OpenCvSharp.Size;

namespace Stitsch
{
    public partial class StitchViewModel : ObservableObject
    {
        private Mat? _stitchingMat;

        [ObservableProperty]
        private string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Stitch");

        [ObservableProperty]
        private int _xClipCount = 108;

        [ObservableProperty]
        private int _yClipCount = 109;

        [ObservableProperty]
        private int _transitionRegionX = 108;

        [ObservableProperty]
        private int _transitionRegionY = 109;

        [ObservableProperty]
        private bool _isBlenderMode = false;

        [ObservableProperty]
        private bool _isBlenderMultiBand = false;

        [ObservableProperty]
        private bool _isIllumination = false;

        [RelayCommand]
        void Load()
        {
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = Root,
                Title = "请选择拼接文件所在的文件夹"
            };

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Root = dialog.FileName;
            }
        }

        [RelayCommand]
        void Apply()
        {
            try
            {
                // 1. 获取图片文件
                var imageFiles = Directory.GetFiles(Root, "*.*")
                    .Where(f => f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show("文件夹中未找到图片");
                    });
                    return;
                }

                // 2. 解析行列
                var regex = new Regex(@"(\d+)_(\d+)");
                var imageData = imageFiles
                    .Select(f => new { File = f, Match = regex.Match(Path.GetFileNameWithoutExtension(f)) })
                    .Where(x => x.Match.Success)
                    .Select(x => (Row: int.Parse(x.Match.Groups[1].Value),
                                  Col: int.Parse(x.Match.Groups[2].Value),
                                  Path: x.File))
                    .OrderBy(i => i.Row)
                    .ThenBy(i => i.Col)
                    .ToList();

                if (imageData.Count == 0)
                {
                    Debug.WriteLine("未能解析到有效的行列命名");
                    return;
                }

                // 3. 最大行列
                int maxRow = imageData.Max(i => i.Row);
                int maxCol = imageData.Max(i => i.Col);

                // 4. 样本图 & 拼接参数
                var mode = ImreadModes.Grayscale;
                var sample = Cv2.ImRead(imageData[0].Path, mode);

                int cropX = 0;
                int cropY = 0;
                int overlapX = 0;
                int overlapY = 0;
                int tileWidth = 0;
                int tileHeight = 0;
                int stitchMatWidth = 0;
                int stitchMatHeight = 0;

                if (IsBlenderMode && (TransitionRegionX > 0 || TransitionRegionY > 0))
                {
                    cropX = TransitionRegionX / 2;
                    cropY = TransitionRegionY / 2;
                    //cropX = XClipCount + TransitionRegionX / 2;//blender模式下，XClipCount裁切为0，TransitionRegionX代替裁切
                    //cropY = YClipCount + TransitionRegionY / 2;
                    overlapX = TransitionRegionX;
                    overlapY = TransitionRegionY;
                    tileWidth = sample.Width - 2 * cropX;
                    tileHeight = sample.Height - 2 * cropY;
                    stitchMatWidth = tileWidth * maxCol - TransitionRegionX * (maxCol - 1);
                    stitchMatHeight = tileHeight * maxRow - TransitionRegionY * (maxRow - 1);
                }
                else
                {
                    cropX = XClipCount;
                    cropY = YClipCount;
                    tileWidth = sample.Width - 2 * cropX;
                    tileHeight = sample.Height - 2 * cropY;
                    stitchMatWidth = tileWidth * maxCol;
                    stitchMatHeight = tileHeight * maxRow;
                }
                _stitchingMat = new Mat(stitchMatHeight, stitchMatWidth, sample.Type(), Scalar.All(0));

                Mat? prevImage = null;
                foreach (var imgInfo in imageData)
                {
                    var img = Cv2.ImRead(imgInfo.Path, mode);

                    if (DisposeIndex != 0)
                        img = ImageFilterDispose(img);//图像滤波

                    if (AdjustExposureModeIndex != 0)
                        img = AdjustExposure(img, prevImage!);//根据上一张图做曝光补偿，再拼接

                    if (IlluminationModeIndex != 0)
                        img = ApplyIllumination(img);//光照一致性

                    using var clip = new Mat(img, new Rect(cropX, cropY, tileWidth, tileHeight));

                    if (!IsBlenderMode)
                    {
                        int startX = (imgInfo.Col - 1) * tileWidth;
                        int startY = (imgInfo.Row - 1) * tileHeight;
                        var pasteRect = new Rect(startX, startY, tileWidth, tileHeight);

                        clip.CopyTo(new Mat(_stitchingMat, pasteRect));
                        SafeSetImage(_stitchingMat);
                    }
                    else
                    {               
                        int startX = (imgInfo.Col - 1) * (tileWidth - overlapX);
                        int startY = (imgInfo.Row - 1) * (tileHeight - overlapY);
                        var pasteRect = new Rect(startX, startY, tileWidth, tileHeight);

                        var row = imgInfo.Row - 1;
                        var col = imgInfo.Col - 1;
                        var dst = _stitchingMat;
                        var cols = maxCol;

                        if (!IsBlenderMultiBand)
                        {
                            if (row == 0 && col == 0)
                            {
                                clip.CopyTo(new Mat(dst, pasteRect));
                            }
                            if ((row % 2 == 0 && col == 0) || (row % 2 == 1 && col == cols - 1))
                            {
                                clip
                                    .BlenderTop(new Mat(dst, new Rect(startX, startY, tileWidth, overlapY)))
                                    .CopyTo(new Mat(dst, pasteRect));
                            }
                            else if (row % 2 == 0)
                            {
                                if (row == 0)
                                {
                                    clip
                                        .BlenderLeft(new Mat(dst, new Rect(startX, startY, overlapX, tileHeight)))
                                        .CopyTo(new Mat(dst, pasteRect));
                                }
                                else
                                {
                                    clip
                                        .BlenderLeft(new Mat(dst, new Rect(startX, startY, overlapX, tileHeight)))
                                        .BlenderTop(new Mat(dst, new Rect(startX, startY, tileWidth, overlapY)))
                                        .CopyTo(new Mat(dst, pasteRect));
                                }
                            }
                            else if (row % 2 == 1)
                            {
                                clip
                                    .BlenderRight(new Mat(dst, new Rect(startX + tileWidth - overlapX, startY, overlapX, tileHeight)))
                                    .BlenderTop(new Mat(dst, new Rect(startX, startY, tileWidth, overlapY)))
                                    .CopyTo(new Mat(dst, pasteRect));
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                        }
                        else
                        {
                            if (row == 0 && col == 0)
                            {
                                clip.CopyTo(new Mat(dst, pasteRect));
                            }
                            if ((row % 2 == 0 && col == 0) || (row % 2 == 1 && col == cols - 1))
                            {
                                clip
                                    .BlenderTopMultiBand(new Mat(dst, new Rect(startX, startY, tileWidth, overlapY)))
                                    .CopyTo(new Mat(dst, pasteRect));
                            }
                            else if (row % 2 == 0)
                            {
                                if (row == 0)
                                {
                                    clip
                                        .BlenderLeftMultiBand(new Mat(dst, new Rect(startX, startY, overlapX, tileHeight)))
                                        .CopyTo(new Mat(dst, pasteRect));
                                }
                                else
                                {
                                    clip
                                        .BlenderLeftMultiBand(new Mat(dst, new Rect(startX, startY, overlapX, tileHeight)))
                                        .BlenderTopMultiBand(new Mat(dst, new Rect(startX, startY, tileWidth, overlapY)))
                                        .CopyTo(new Mat(dst, pasteRect));
                                }
                            }
                            else if (row % 2 == 1)
                            {
                                clip
                                    .BlenderRightMultiBand(new Mat(dst, new Rect(startX + tileWidth - overlapX, startY, overlapX, tileHeight)))
                                    .BlenderTopMultiBand(new Mat(dst, new Rect(startX, startY, tileWidth, overlapY)))
                                    .CopyTo(new Mat(dst, pasteRect));
                            }
                            else
                            {
                                throw new NotImplementedException();
                            }
                        }
                        SafeSetImage(dst);
                    }

                    prevImage = img.Clone();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        [RelayCommand]
        void Save()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "保存图像",
                Filter = "PNG 图像 (*.png)|*.png|JPEG 图像 (*.jpg;*.jpeg)|*.jpg;*.jpeg|TIFF 图像 (*.tif)|*.tif",
                DefaultExt = ".png",
                AddExtension = true,
                FileName = $"Stitching_{DateTime.Now:yyyyMMdd_HHmmss}" // 默认带时间戳文件名
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    //_stitchingMat!.SaveImage(dialog.FileName);
                    DisplayCurrent!.Display.SaveImage(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

    }

    public partial class StitchViewModel
    {
        [ObservableProperty]
        private int _adjustExposureModeIndex;

        [ObservableProperty]
        private List<string> _adjustExposureMode = new()
        {
            "None",
            "Mean",
            "Median",
            "HistogramMatching",
            "Gamma",
        };

        private Mat AdjustExposure(Mat imgA, Mat imgB)
        {
            return AdjustExposureModeIndex switch
            {
                1 => AdjustExposureby_Mean(imgA, imgB),
                2 => AdjustExposure_Median(imgA, imgB),
                3 => AdjustExposure_HistogramMatching(imgA, imgB),
                4 => AdjustExposure_Gamma(imgA, imgB),
                _ => AdjustExposureby_Mean(imgA, imgB),
            };
        }

        static Mat AdjustExposureby_Mean(Mat imgA, Mat imgB)
        {
            if (imgB == null) return imgA;

            // 转灰度
            Mat grayA = EnsureGray(imgA);
            Mat grayB = EnsureGray(imgB);

            // 计算均值
            Scalar meanA = Cv2.Mean(grayA);
            Scalar meanB = Cv2.Mean(grayB);

            double alpha = meanB.Val0 / meanA.Val0;

            // 调整 A 的亮度
            Mat adjusted = new Mat();
            imgA.ConvertTo(adjusted, imgA.Type(), alpha, 0);
            return adjusted;

        }

        static Mat AdjustExposure_Median(Mat imgA, Mat imgB)
        {
            if (imgB == null) return imgA;

            // 灰度图变一维
            Mat grayA = EnsureGray(imgA).Reshape(1, 1);
            Mat grayB = EnsureGray(imgB).Reshape(1, 1);

            // 拷贝到托管数组
            byte[] pixelsA;
            grayA.GetArray(out pixelsA);
            Array.Sort(pixelsA);
            double medianA = pixelsA[pixelsA.Length / 2];

            byte[] pixelsB;
            grayB.GetArray(out pixelsB);
            Array.Sort(pixelsB);
            double medianB = pixelsB[pixelsB.Length / 2];

            double alpha = medianB / (medianA + 1e-6);

            Mat adjusted = new Mat();
            imgA.ConvertTo(adjusted, imgA.Type(), alpha, 0);
            return adjusted;
        }

        static Mat AdjustExposure_HistogramMatching(Mat imgA, Mat imgB)
        {
            if (imgB == null) return imgA;

            Mat grayA = EnsureGray(imgA);
            Mat grayB = EnsureGray(imgB);

            // 计算累计直方图
            Mat histA = new Mat();
            Mat histB = new Mat();
            int histSize = 256;
            Rangef range = new Rangef(0, 256);

            Cv2.CalcHist(new[] { grayA }, new[] { 0 }, null, histA, 1, new[] { histSize }, new[] { range });
            Cv2.CalcHist(new[] { grayB }, new[] { 0 }, null, histB, 1, new[] { histSize }, new[] { range });

            histA /= grayA.Total();
            histB /= grayB.Total();

            float[] cdfA = new float[256];
            float[] cdfB = new float[256];
            cdfA[0] = histA.Get<float>(0);
            cdfB[0] = histB.Get<float>(0);
            for (int i = 1; i < 256; i++)
            {
                cdfA[i] = cdfA[i - 1] + histA.Get<float>(i);
                cdfB[i] = cdfB[i - 1] + histB.Get<float>(i);
            }

            // 构建映射表
            byte[] lookup = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                int j = 0;
                while (j < 255 && cdfA[i] > cdfB[j]) j++;
                lookup[i] = (byte)j;
            }

            Mat lut = new Mat(1, 256, MatType.CV_8U);
            IntPtr ptr = lut.Data;
            Marshal.Copy(lookup, 0, ptr, lookup.Length);

            Mat adjusted = new Mat();
            Cv2.LUT(grayA, lut, adjusted);

            if (imgA.Channels() > 1)
                Cv2.CvtColor(adjusted, adjusted, ColorConversionCodes.GRAY2BGR);

            return adjusted;
        }

        static Mat AdjustExposure_Gamma(Mat imgA, Mat imgB)
        {
            if (imgB == null) return imgA;

            Mat grayA = EnsureGray(imgA);
            Mat grayB = EnsureGray(imgB);

            double meanA = Cv2.Mean(grayA).Val0 / 255.0;
            double meanB = Cv2.Mean(grayB).Val0 / 255.0;

            double gamma = Math.Log(meanB + 1e-6) / Math.Log(meanA + 1e-6);

            Mat adjusted = new Mat();
            imgA.ConvertTo(adjusted, MatType.CV_32F, 1.0 / 255);
            Cv2.Pow(adjusted, gamma, adjusted);
            adjusted.ConvertTo(adjusted, imgA.Type(), 255);

            return adjusted;
        }

        static Mat AdjustExposure_Overlap(Mat imgA, Mat imgB, Rect overlapRegion)
        {
            if (imgB == null) return imgA;

            Mat overlapA = new Mat(imgA, overlapRegion);
            Mat overlapB = new Mat(imgB, overlapRegion);

            Mat grayA = EnsureGray(overlapA);
            Mat grayB = EnsureGray(overlapB);

            double meanA = Cv2.Mean(grayA).Val0;
            double meanB = Cv2.Mean(grayB).Val0;

            double alpha = meanB / meanA;

            Mat adjusted = new Mat();
            imgA.ConvertTo(adjusted, imgA.Type(), alpha, 0);
            return adjusted;
        }

        static Mat EnsureGray(Mat src)
        {
            // 如果是 8UC1 灰度图，直接返回
            if (src.Type() == MatType.CV_8UC1)
                return src.Clone();

            // 如果是三通道或其他情况，转灰度
            Mat gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            return gray;
        }

    }

    public partial class StitchViewModel
    {
        [ObservableProperty]
        private int _illuminationModeIndex;

        [ObservableProperty]
        private List<string> _illuminationMode = new()
        {
            "None",
            "Low-Frequency",
            "Gaussian Low-frequency Division",
            "TopHat",
            "CLAHE",
            //"Homomorphic"
        };

        private Mat ApplyIllumination(Mat img)
        {
            return IlluminationModeIndex switch
            {
                1 => IlluminationCompensate(img),
                2 => IlluminationCompensateGray(img),
                3 => IlluminationCompensate_TopHat(img),
                4 => IlluminationCompensate_CLAHE(img),
                //5 => IlluminationCompensate_Homomorphic(img),
                _ => IlluminationCompensate(img),
            };
        }

        static Mat IlluminationCompensate(Mat img)
        {
            Mat lowFreq = new Mat();
            Cv2.GaussianBlur(img, lowFreq, new Size(51, 51), 0);

            // 计算比例因子
            Mat ratio = new Mat();
            Cv2.Divide(lowFreq, Cv2.Mean(lowFreq), ratio);

            // 修正图像
            Mat result = new Mat();
            Cv2.Divide(img, ratio, result);
            return result;
        }

        static Mat IlluminationCompensateGray(Mat grayImg)
        {
            if (grayImg.Type() != MatType.CV_8UC1)
                throw new ArgumentException("输入必须是8UC1灰度图");

            // 转换成浮点，方便计算
            Mat grayFloat = new Mat();
            grayImg.ConvertTo(grayFloat, MatType.CV_32F);

            // 大核高斯模糊提取低频光照（核大小根据图像大小调整）
            Mat illumination = new Mat();
            int ksize = (grayImg.Width / 10) | 1; // 保证奇数
            ksize = Math.Max(ksize, 31); // 最小31x31
            Cv2.GaussianBlur(grayFloat, illumination, new Size(ksize, ksize), 0);

            // 防止除零，阈值
            double eps = 1e-3;
            Cv2.Max(illumination, eps, illumination);

            // 归一化处理：原图除以估计光照
            Mat compensated = new Mat();
            Cv2.Divide(grayFloat, illumination, compensated);

            // 归一化回 0~255
            Cv2.Normalize(compensated, compensated, 0, 255, NormTypes.MinMax);

            Mat result = new Mat();
            compensated.ConvertTo(result, MatType.CV_8UC1);

            return result;
        }

        static Mat IlluminationCompensate_TopHat(Mat grayImg)
        {
            if (grayImg.Type() != MatType.CV_8UC1)
                throw new ArgumentException("输入必须是8UC1灰度图");

            int kernelSize = Math.Max(grayImg.Width / 10, 31) | 1;
            Mat element = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(kernelSize, kernelSize));

            Mat topHat = new Mat();
            Cv2.MorphologyEx(grayImg, topHat, MorphTypes.TopHat, element);

            Mat result = new Mat();
            Cv2.Normalize(topHat, result, 0, 255, NormTypes.MinMax);
            return result;
        }

        static Mat IlluminationCompensate_CLAHE(Mat grayImg)
        {
            if (grayImg.Type() != MatType.CV_8UC1)
                throw new ArgumentException("输入必须是8UC1灰度图");

            var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
            Mat result = new Mat();
            clahe.Apply(grayImg, result);
            return result;
        }

        static Mat IlluminationCompensate_Homomorphic(Mat grayImg)
        {
            if (grayImg.Type() != MatType.CV_8UC1)
                throw new ArgumentException("输入必须是8UC1灰度图");

            // 转 float 并 log 变换
            Mat grayFloat = new Mat();
            grayImg.ConvertTo(grayFloat, MatType.CV_32F);
            Cv2.Add(grayFloat, 1, grayFloat); // 避免 log(0)
            Cv2.Log(grayFloat, grayFloat);

            // DFT
            Mat dft = new Mat();
            Cv2.Dft(grayFloat, dft, DftFlags.ComplexOutput);

            // 准备高通掩膜
            int rows = dft.Rows;
            int cols = dft.Cols;
            Mat maskLow = new Mat(rows, cols, MatType.CV_32F);

            // 生成以中心为低频的高通掩膜
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double dx = x - cols / 2.0;
                    double dy = y - rows / 2.0;
                    double radius = Math.Sqrt(dx * dx + dy * dy);
                    double sigma = Math.Min(rows, cols) / 4.0; // 控制截止频率
                    maskLow.Set(y, x, 1.0 - Math.Exp(-(radius * radius) / (2 * sigma * sigma)));
                }
            }

            // 两通道掩膜
            Mat[] maskChannels = { maskLow, maskLow };
            Mat mask2C = new Mat();
            Cv2.Merge(maskChannels, mask2C);

            // 滤波
            Cv2.Multiply(dft, mask2C, dft);

            // 反 DFT
            Mat idft = new Mat();
            Cv2.Dft(dft, idft, DftFlags.Inverse | DftFlags.RealOutput | DftFlags.Scale);

            // exp 反变换
            Cv2.Exp(idft, idft);
            Mat result = new Mat();
            idft.ConvertTo(result, MatType.CV_8UC1);

            return result;
        }

    }

    public partial class StitchViewModel
    {
        [ObservableProperty]
        private int _disposeIndex = 0;

        [ObservableProperty]
        private List<string> _disposeMode = new()
        {
            "None",                   // 0 无处理            
            "Gaussian Blur",          // 1 高斯滤波
            "Box Filter",             // 2 均值滤波
            "Median Blur",            // 3 中值滤波
            "Bilateral Filter",       // 4 双边滤波
            "Non-Local Means",        // 5 非局部均值去噪
            "Edge Preserving Filter", // 6 边缘保留滤波
            "Morphological Open"      // 7 形态学开运算
        };

        private Mat ImageFilterDispose(Mat img)
        {
            Mat src = img.Clone();
            Mat filtered = new();
            switch (DisposeIndex)
            {
                case 0: // 无处理
                    return img;
                case 1: // 高斯滤波
                    Cv2.GaussianBlur(src, filtered, new Size(5, 5), 0);
                    break;
                case 2: // 均值滤波
                    Cv2.Blur(src, filtered, new Size(5, 5));
                    break;
                case 3: // 中值滤波
                    Cv2.MedianBlur(src, filtered, 5);
                    break;
                case 4: // 双边滤波
                    Cv2.BilateralFilter(src, filtered, 9, 75, 75);
                    break;
                case 5: // 非局部均值去噪（需8U）
                    if (src.Type() != MatType.CV_8UC1)
                        src.ConvertTo(src, MatType.CV_8UC1, 1.0 / 256);
                    Cv2.FastNlMeansDenoising(src, filtered, 10, 7, 21);
                    break;
                case 6: // 边缘保留滤波
                    Cv2.EdgePreservingFilter(src, filtered, EdgePreservingMethods.RecursFilter, 60, 0.4f);
                    break;
                case 7: // 形态学开运算（去亮点噪声）
                    Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
                    Cv2.MorphologyEx(src, filtered, MorphTypes.Open, kernel);
                    break;
                default:
                    return img;
            }
            return filtered;
        }

    }

    public partial class StitchViewModel
    {
        void SafeSetImage(Mat stitching)
        {
            // 更新 UI 显示
            Application.Current?.Dispatcher.Invoke(() =>
            {
                using Mat compressedImage = CompressImage_Resize(stitching);

                if (compressedImage.Type() == MatType.CV_8UC3)
                {
                    // 原始图像 _stitchingMat 为 8UC3
                    var grayMat = new Mat();
                    Cv2.CvtColor(compressedImage, compressedImage, ColorConversionCodes.BGR2GRAY);
                }

                DisplayCurrent.Original = compressedImage.Clone();

            }, DispatcherPriority.Background);

        }

        [ObservableProperty]
        private DisplayModel _displayCurrent = new();

        /// <summary>
        /// 缩放系数
        /// 节省拼接图像资源
        /// </summary>
        [ObservableProperty]
        private int _scaleResize = 1;

        /// <summary>
        /// 图像压缩
        /// </summary>
        /// <param name="originalImage"></param>
        /// <returns></returns>
        Mat CompressImage_Resize(Mat originalImage)
        {
            //1-通过 Cv2.Resize 方法将图像的分辨率缩小到原来的四分之一（宽度和高度都缩小到原来的四分之一，总像素数为原来的 1/16）。
            //这是一种简单的图像尺寸压缩，主要通过降低图像的分辨率来减少内存占用
            Mat mat = new();
            Cv2.Resize(originalImage, mat, new OpenCvSharp.Size(originalImage.Width / ScaleResize, originalImage.Height / ScaleResize));
            return mat;
        }
    }
}
