using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.WindowsAPICodePack.Dialogs;
using OpenCvSharp;
using Simscop.Pl.Core.Models.Advanced;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using Point = OpenCvSharp.Point;
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
                var imageFiles = Directory.GetFiles(Root, "*.*").Where(f => 
                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)|| 
                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)|| 
                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)|| 
                f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)) 
                    .ToList();

                if (imageFiles.Count == 0)
                {
                    Debug.WriteLine("文件夹中未找到图片");
                    return;
                }

                // 正则解析行列
                var regex = new Regex(@"(\d+)_(\d+)");
                var imageData = new List<(int Row, int Col, string Path)>();

                foreach (var file in imageFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var match = regex.Match(fileName);
                    if (match.Success)
                    {
                        int row = int.Parse(match.Groups[1].Value);
                        int col = int.Parse(match.Groups[2].Value);
                        imageData.Add((row, col, file));
                    }
                }

                if (imageData.Count == 0)
                {
                    Debug.WriteLine("未能解析到有效的行列命名");
                    return;
                }

                // 获取最大行列
                int maxRow = imageData.Max(i => i.Row);
                int maxCol = imageData.Max(i => i.Col);

                var mode = ImreadModes.Grayscale;

                var sample = Cv2.ImRead(imageData[0].Path, mode);
                int tileWidth = sample.Width - XClipCount * 2;
                int tileHeight = sample.Height - YClipCount * 2;

                //// 创建大图
                //var (Crop, Overlap, TileSize, MatSize) = CalculateTileInfo(XClipCount, YClipCount, sample.Width, sample.Height, maxCol, maxRow, IsBlenderMode, TransitionRegion);
                //_stitchingMat = new Mat( MatSize.Height, MatSize.Width, sample.Type(), Scalar.All(0));

                //创建大图
               _stitchingMat = new Mat(tileHeight * maxRow, tileWidth * maxCol, sample.Type(), Scalar.All(0));

                // 拼接
                foreach (var imgInfo in imageData)
                {
                    var img = Cv2.ImRead(imgInfo.Path, mode);

                    Mat imgDispose = new();
                    imgDispose = ImageFilterDispose(img);

                    // 裁切 ROI
                    var roiRect = new Rect(XClipCount, YClipCount, tileWidth, tileHeight);
                    using var clip = new Mat(imgDispose, roiRect);

                    int startX = (imgInfo.Col - 1) * tileWidth;
                    int startY = (imgInfo.Row - 1) * tileHeight;

                    clip.CopyTo(new Mat(_stitchingMat, new Rect(startX, startY, tileWidth, tileHeight)));
                }

                // 更新 UI 显示
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    using Mat compressedImage = CompressImage_Resize(_stitchingMat);

                    if (compressedImage.Type() == MatType.CV_8UC3)
                    {
                        // 原始图像 _stitchingMat 为 8UC3
                        var grayMat = new Mat();
                        Cv2.CvtColor(compressedImage, compressedImage, ColorConversionCodes.BGR2GRAY);
                    }

                    DisplayCurrent.Original = compressedImage.Clone();

                }, DispatcherPriority.Background);

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
        private (Rect Crop, Point Overlap, Size TileSize, Size MatSize) CalculateTileInfo(int xClipCount, int yClipCount, int subgraphWidth, int subgraphHeight, int cols, int rows, bool isBlenderMode = false, int transitionRegion = 0)
        {
            int cropX = xClipCount, cropY = yClipCount, overlapX = 0, overlapY = 0;
            if (isBlenderMode && transitionRegion > 0)
            {
                cropX = (int)(transitionRegion * 0.5 + xClipCount);
                cropY = (int)(transitionRegion * 0.5 + yClipCount);
                overlapX = (transitionRegion - cropX + xClipCount) * 2;
                overlapY = (transitionRegion - cropY + yClipCount) * 2;
            }

            int tileWidth = subgraphWidth - 2 * cropX;
            int tileHeight = subgraphHeight - 2 * cropY;

            if (tileWidth <= 0 || tileHeight <= 0)
                throw new ArgumentException("xClipCount or yClipCount or transitionRegion not valid");

            int matWidth = tileWidth * cols - overlapX * (cols - 1);
            int matHeight = tileHeight * rows - overlapY * (rows - 1);

            return (
                new Rect(cropX, cropY, tileWidth, tileHeight),
                new Point(overlapX, overlapY),
                new Size(tileWidth, tileHeight),
                new Size(matWidth, matHeight));
        }

    }

    public partial class StitchViewModel
    {
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
