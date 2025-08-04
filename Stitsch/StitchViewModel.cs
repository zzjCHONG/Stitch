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
        private int _xClipCount = 112;

        [ObservableProperty]
        private int _yClipCount = 112;

        [ObservableProperty]
        private int _transitionRegion = 10;

        [ObservableProperty]
        private bool _isBlenderMode = false;

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

                if (IsBlenderMode && TransitionRegion > 0)
                {
                    cropX = XClipCount + TransitionRegion / 2;
                    cropY = YClipCount + TransitionRegion / 2;
                    overlapX = TransitionRegion;
                    overlapY = TransitionRegion;
                    tileWidth = sample.Width - 2 * cropX;
                    tileHeight = sample.Height - 2 * cropY;
                    stitchMatWidth = tileWidth * maxCol - TransitionRegion * (maxCol - 1);
                    stitchMatHeight = tileHeight * maxRow - TransitionRegion * (maxRow - 1);
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

                        if (row == 0 && col == 0)
                        {
                            clip.CopyTo(new Mat(dst, pasteRect));
                        }
                        if ((row % 2 == 0 && col == 0) || (row % 2 == 1 && col == maxCol - 1))
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
