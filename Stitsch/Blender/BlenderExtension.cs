using OpenCvSharp;

namespace Simscop.Pl.Core.Models
{
    public static class BlenderExtension
    {
        /// <summary>
        /// 两张图像拼接
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        /// <param name="start">
        /// dst 需要拼接的坐标（相对于src的坐上点)
        /// </param>
        /// <returns></returns>
        //public static Mat Combine(this Mat src, Mat dst, OpenCvSharp.Point start)
        //{
        //    var top = int.Min(0, start.Y);
        //    var bottom = int.Max(src.Height, start.Y + dst.Height);
        //    var left = int.Min(0, start.X);
        //    var right = int.Max(dst.Width, start.X + dst.Width);

        //    var width = right - left;
        //    var height = bottom - top;

        //    var combine = new Mat(new OpenCvSharp.Size(width, height), src.Type(), new Scalar(0));
        //    src.CopyTo(combine[new OpenCvSharp.Rect(int.Abs(left), int.Abs(top), src.Width, src.Height)]);
        //    dst.CopyTo(combine[new OpenCvSharp.Rect(start.X - left, start.Y - top, dst.Width, dst.Height)]);

        //    return combine;
        //}

        /// <summary>
        /// 加权线性融合左边roi区域的图像
        /// </summary>
        /// <param name="src"></param>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <summary>
        /// 加权线性融合左边roi区域的图像
        /// </summary>
        /// <param name="src"></param>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Mat BlenderLeft(this Mat src, Mat roi)
        {
            if (src.Height != roi.Height)
                throw new ArgumentException();

            var overlap = roi.Width;
            var dst = src.Clone();
            roi = roi.Clone();

            for (var i = 0; i < overlap; i++)
            {
                var scale = i * (1.0 / overlap);

                roi[0, roi.Height, i, i + 1] *= (1 - scale);
                dst[0, dst.Height, i, i + 1] *= scale;
            }

            var dstRoi = dst[0, dst.Height, 0, overlap];
            var blender = (roi + dstRoi).ToMat();

            blender.CopyTo(dst[0, dst.Height, 0, overlap]);

            return dst;
        }

        /// <summary>
        /// 加权线性融合右边roi区域的图像
        /// </summary>
        /// <param name="src"></param>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Mat BlenderRight(this Mat src, Mat roi)
        {
            if (src.Height != roi.Height)
                throw new ArgumentException();

            var overlap = roi.Width;
            var dst = src.Clone();
            roi = roi.Clone();

            var start = src.Width - roi.Width;

            for (var i = 0; i < overlap; i++)
            {
                var scale = i * (1.0 / overlap);

                roi[0, roi.Height, i, i + 1] *= scale;
                dst[0, dst.Height, start + i, start + i + 1] *= (1 - scale);
            }

            var dstRoi = dst[0, dst.Height, dst.Width - overlap, dst.Width];
            var blender = (roi + dstRoi).ToMat();

            blender.CopyTo(dst[0, dst.Height, dst.Width - overlap, dst.Width]);

            return dst;
        }

        /// <summary>
        /// 加权线性融合上边roi区域的图像
        /// </summary>
        /// <param name="src"></param>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Mat BlenderTop(this Mat src, Mat roi)
        {
            if (src.Width != roi.Width)
                throw new ArgumentException();

            var overlap = roi.Height;
            var dst = src.Clone();
            roi = roi.Clone();

            for (var i = 0; i < overlap; i++)
            {
                var scale = i * (1.0 / overlap);

                roi[i, i + 1, 0, roi.Width] *= (1 - scale);
                dst[i, i + 1, 0, dst.Width] *= scale;
            }

            dst[0, overlap, 0, dst.Width] += roi;

            return dst;
        }

        /// <summary>
        /// 加权线性融合下边roi区域的图像
        /// </summary>
        /// <param name="src"></param>
        /// <param name="roi"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static Mat BlenderBottom(this Mat src, Mat roi)
        {
            if (src.Width != roi.Width)
                throw new ArgumentException();

            var overlap = roi.Height;
            var dst = src.Clone();
            roi = roi.Clone();

            var start = dst.Height - roi.Height;

            for (var i = 0; i < overlap; i++)
            {
                var scale = i * (1.0 / overlap);

                roi[i, i + 1, 0, roi.Width] *= scale;
                dst[start + i, start + i + 1, 0, dst.Width] *= (1 - scale);
            }

            dst[start, start + overlap, 0, dst.Width] += roi;

            return dst;
        }

    }

    public static class MultiBandBlenderExtension
    {
        public static Mat BlenderLeftMultiBand(this Mat src, Mat roi, int levels = 5)
        {
            if (src.Height != roi.Height)
                throw new ArgumentException();
            var dst = src.Clone();
            var dstRoi = new Mat(dst, new Rect(0, 0, roi.Width, roi.Height));
            var mask = MultiBandBlenderExtension.CreateHorizontalMask(roi.Size(), leftToRight: true);
            var blended = MultiBandBlenderExtension.MultiBandBlend(dstRoi, roi, mask, levels);
            blended.CopyTo(dstRoi);
            return dst;
        }

        public static Mat BlenderRightMultiBand(this Mat src, Mat roi, int levels = 5)
        {
            if (src.Height != roi.Height)
                throw new ArgumentException();
            var dst = src.Clone();
            int startX = src.Width - roi.Width;
            var dstRoi = new Mat(dst, new Rect(startX, 0, roi.Width, roi.Height));
            var mask = MultiBandBlenderExtension.CreateHorizontalMask(roi.Size(), leftToRight: false);
            var blended = MultiBandBlenderExtension.MultiBandBlend(dstRoi, roi, mask, levels);
            blended.CopyTo(dstRoi);
            return dst;
        }

        public static Mat BlenderTopMultiBand(this Mat src, Mat roi, int levels = 5)
        {
            if (src.Width != roi.Width)
                throw new ArgumentException();
            var dst = src.Clone();
            var dstRoi = new Mat(dst, new Rect(0, 0, roi.Width, roi.Height));
            var mask = MultiBandBlenderExtension.CreateVerticalMask(roi.Size(), topToBottom: true);
            var blended = MultiBandBlenderExtension.MultiBandBlend(dstRoi, roi, mask, levels);
            blended.CopyTo(dstRoi);
            return dst;
        }

        public static Mat BlenderBottomMultiBand(this Mat src, Mat roi, int levels = 5)
        {
            if (src.Width != roi.Width)
                throw new ArgumentException();
            var dst = src.Clone();
            int startY = src.Height - roi.Height;
            var dstRoi = new Mat(dst, new Rect(0, startY, roi.Width, roi.Height));
            var mask = MultiBandBlenderExtension.CreateVerticalMask(roi.Size(), topToBottom: false);
            var blended = MultiBandBlenderExtension.MultiBandBlend(dstRoi, roi, mask, levels);
            blended.CopyTo(dstRoi);
            return dst;
        }

        /// <summary>
        /// 拉普拉斯金字塔多频段融合
        /// </summary>
        public static Mat MultiBandBlend(Mat img1, Mat img2, Mat mask, int levels = 5)
        {
            // 转换到 float32，避免计算溢出
            Mat img1f = new Mat();
            Mat img2f = new Mat();
            Mat maskf = new Mat();
            img1.ConvertTo(img1f, MatType.CV_32F);
            img2.ConvertTo(img2f, MatType.CV_32F);
            mask.ConvertTo(maskf, MatType.CV_32F, 1.0 / 255.0);

            // 1. 构建高斯金字塔
            var g1 = new List<Mat> { img1f };
            var g2 = new List<Mat> { img2f };
            var gm = new List<Mat> { maskf };

            for (int i = 0; i < levels; i++)
            {
                Mat d1 = new Mat(), d2 = new Mat(), dm = new Mat();
                Cv2.PyrDown(g1[i], d1);
                Cv2.PyrDown(g2[i], d2);
                Cv2.PyrDown(gm[i], dm);
                g1.Add(d1);
                g2.Add(d2);
                gm.Add(dm);
            }

            // 2. 构建拉普拉斯金字塔
            var l1 = new List<Mat>();
            var l2 = new List<Mat>();

            for (int i = 0; i < levels; i++)
            {
                Mat u1 = new Mat(), u2 = new Mat();
                Cv2.PyrUp(g1[i + 1], u1, g1[i].Size());
                Cv2.PyrUp(g2[i + 1], u2, g2[i].Size());

                l1.Add(g1[i] - u1);
                l2.Add(g2[i] - u2);
            }
            l1.Add(g1[levels]);
            l2.Add(g2[levels]);

            // 3. 每一层融合
            var ls = new List<Mat>();
            for (int i = 0; i <= levels; i++)
            {
                // 确保 mask 是浮点类型，范围 0~1
                Mat maskFloat = new Mat();
                if (gm[i].Type() != MatType.CV_32F)
                    gm[i].ConvertTo(maskFloat, MatType.CV_32F, 1.0 / 255.0);
                else
                    maskFloat = gm[i];

                // 计算反掩码 (1 - mask)
                Mat invMask = new Mat();
                Cv2.Subtract(1.0, maskFloat, invMask);

                // 融合当前层
                Mat blended = l1[i].Mul(maskFloat) + l2[i].Mul(invMask);

                ls.Add(blended);
            }


            // 4. 重建图像
            Mat blendedImg = ls[levels];
            for (int i = levels - 1; i >= 0; i--)
            {
                Mat up = new Mat();
                Cv2.PyrUp(blendedImg, up, ls[i].Size());
                blendedImg = up + ls[i];
            }

            blendedImg.ConvertTo(blendedImg, img1.Type());
            return blendedImg;
        }

        /// <summary>
        /// 生成水平渐变掩码
        /// </summary>
        public static Mat CreateHorizontalMask(Size size, bool leftToRight = true)
        {
            Mat mask = new Mat(size, MatType.CV_8UC1);
            for (int x = 0; x < size.Width; x++)
            {
                byte val = (byte)(255.0 * x / (size.Width - 1));
                if (!leftToRight) val = (byte)(255 - val);
                mask.Col(x).SetTo(val);
            }
            return mask;
        }

        /// <summary>
        /// 生成垂直渐变掩码
        /// </summary>
        public static Mat CreateVerticalMask(Size size, bool topToBottom = true)
        {
            Mat mask = new Mat(size, MatType.CV_8UC1);
            for (int y = 0; y < size.Height; y++)
            {
                byte val = (byte)(255.0 * y / (size.Height - 1));
                if (!topToBottom) val = (byte)(255 - val);
                mask.Row(y).SetTo(val);
            }
            return mask;
        }
    }

}
