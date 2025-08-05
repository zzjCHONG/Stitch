using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using OpenCvSharp;
using Simscop.Pl.Core.Models;
using Simscop.Pl.Core.Models.Advanced;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
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
        private bool _isAdjustExposure = false;

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
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    Debug.WriteLine("文件夹中未找到图片");
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
                    cropX = 0 + TransitionRegionX / 2;
                    cropY = 0 + TransitionRegionY / 2;
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

                foreach (var imgInfo in imageData)
                {
                    var img = Cv2.ImRead(imgInfo.Path, mode);

                    if (DisposeIndex != 0)
                        img = ImageFilterDispose(img);//图像滤波

                    if (IsAdjustExposure)
                        img = AdjustExposure(img, sample);//根据第一张图做曝光补偿，再拼接

                    if (IsIllumination)
                        img = IlluminationCompensateGray(img);//光照一致性

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

                        bool isBlenderMultiBand=true;

                        if (!isBlenderMultiBand)
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
        private int _disposeIndex = 0;

        [ObservableProperty]
        private List<string> _disposeMode = new()
        {
            "None",
            "Gaussian Blur",
            "Box Filter",
        };

        private Mat ImageFilterDispose(Mat img)
        {
            Mat mat16u = img.Clone();
            Mat filtered = new();
            switch (DisposeIndex)
            {
                case 0:
                    return img;
                case 1:
                    Cv2.GaussianBlur(mat16u, filtered, new Size(5, 5), 0);
                    break;
                case 2:
                    Cv2.Blur(mat16u, filtered, new Size(5, 5));
                    break;
            }
            return filtered;
        }
    }

    public partial class StitchViewModel
    {
        static Mat AdjustExposure(Mat imgA, Mat imgB)
        {
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

        Mat IlluminationCompensateGray(Mat grayImg)
        {
            if (grayImg.Type() != MatType.CV_8UC1)
                throw new ArgumentException("输入必须是8UC1灰度图");

            // 转换成浮点，方便计算
            Mat grayFloat = new Mat();
            grayImg.ConvertTo(grayFloat, MatType.CV_32F);

            // 大核高斯模糊提取低频光照（核大小根据图像大小调整）
            Mat illumination = new Mat();
            int ksize = (grayImg.Width / 10) | 1; // 保证奇数
            ksize = Math.Max(ksize, 1); // 最小31x31
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
