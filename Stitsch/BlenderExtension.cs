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
}
