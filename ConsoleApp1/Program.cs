using OpenCvSharp;

namespace ConsoleApp1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //string file1 = @"C:\\Users\\Administrator\\Desktop\\STITCH\\12\\1_2.tif";
            //string file2 = @"C:\Users\Administrator\Desktop\STITCH\1\1_2.tif";
            //var sample1 = Cv2.ImRead(file1, ImreadModes.Grayscale);
            //var sample2 = Cv2.ImRead(file2, ImreadModes.Grayscale);
            //var mat = AdjustExposure(sample1, sample2);
            //mat.SaveImage("C:\\Users\\Administrator\\Desktop\\STITCH\\0.png");

            string file1 = @"C:\\Users\\Administrator\\Desktop\\STITCH\\12\\1_2.tif";
            var sample1 = Cv2.ImRead(file1, ImreadModes.Grayscale);
            var mat = IlluminationCompensate(sample1);
            mat.SaveImage("C:\\Users\\Administrator\\Desktop\\STITCH\\1.png");
        }

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
    }
}
