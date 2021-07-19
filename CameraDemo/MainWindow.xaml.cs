using FFmpeg.AutoGen;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace CameraDemo
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public unsafe partial class MainWindow : Window
    {
        private IntPtr window = IntPtr.Zero;
        private IntPtr renderer = IntPtr.Zero;
        private IntPtr texture = IntPtr.Zero;
        private SDL.SDL_Rect rect;
        int width = 640;
        int height = 480;
        public MainWindow()
        {
            InitializeComponent();
            string current = Environment.CurrentDirectory;
            string probe = Path.Combine("FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");
            string ffmpegBinaryPath = Path.Combine(current, probe);
            if (Directory.Exists(ffmpegBinaryPath))
            {
                ffmpeg.RootPath = ffmpegBinaryPath;
            }
            _ = SDL.SDL_Init(SDL.SDL_INIT_VIDEO);
            window = SDL.SDL_CreateWindowFrom(PANEL.Handle);
            new Thread(() =>
            {
                captureFrame("rtmp://192.168.100.27:1935/live/device_cxj_test", "USB Camera");
            }).Start();
        }

        private void WFH_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (renderer != IntPtr.Zero && window != IntPtr.Zero)
            {
                int viewW = (int)WFH.ActualWidth;
                int viewH = (int)WFH.ActualHeight;
                SDL.SDL_GetWindowSize(window, out int w, out int h);
                if (viewH > viewW)
                {
                    int newWidth = viewW;
                    int newHeight = height * newWidth / width;
                    SDL.SDL_SetWindowSize(window, newWidth, newHeight);
                    rect = new SDL.SDL_Rect() { x = 0, y = 0, w = newWidth, h = newHeight };
                }
                else
                {
                    int newHeight = viewH;
                    int newWidth = width * newHeight / height;
                    SDL.SDL_SetWindowSize(window, newWidth, newHeight);
                    rect = new SDL.SDL_Rect() { x = 0, y = 0, w = newWidth, h = newHeight };
                }
            }
        }
        
        void captureFrame(string url,string cameraName)
        {
            
            _ = ffmpeg.avformat_network_init();
            ffmpeg.avdevice_register_all();
            AVInputFormat* ifmt = ffmpeg.av_find_input_format("dshow");
            AVFormatContext* ifmt_ctx = null;
            AVFormatContext* ofmt_ctx = null;
            AVCodec* in_codec = null;
            AVCodec* out_codec = null;
            AVCodecContext* in_codec_ctx = null;
            AVCodecContext* out_codec_ctx = null;
            AVStream* in_stream = null;
            AVStream* out_stream = null;

            if (0 > ffmpeg.avformat_open_input(&ifmt_ctx, $"video={cameraName}", ifmt, null))
            {
                Debug.WriteLine("failed open input file\n");
                return;
            }
            if (0 > ffmpeg.avformat_find_stream_info(ifmt_ctx, null))
            {
                Debug.WriteLine("failed find stream info\n");
                ffmpeg.avformat_close_input(&ifmt_ctx);
                return;
            }

            int stream_index = -1;
            stream_index = ffmpeg.av_find_best_stream(ifmt_ctx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            if (-1 == stream_index)
            {
                Debug.WriteLine("failed find stream\n");
                ffmpeg.avformat_close_input(&ifmt_ctx);
                return;
            }
            in_codec = ffmpeg.avcodec_find_decoder(ifmt_ctx->streams[stream_index]->codecpar->codec_id);
            if (null == in_codec)
            {
                Debug.WriteLine("not find encoder\n");
                ffmpeg.avformat_close_input(&ifmt_ctx);
                return;
            }

            in_stream = ffmpeg.avformat_new_stream(ifmt_ctx, in_codec);
            in_codec_ctx = ffmpeg.avcodec_alloc_context3(in_codec);
            AVDictionary* codec_options = null;           
            _ = ffmpeg.av_dict_set(&codec_options, "framerate", "30", 0);
            _ = ffmpeg.av_dict_set(&codec_options, "preset", "ultrafast", 0);
            _ = ffmpeg.av_dict_set(&codec_options, "tune", "zerolatency", 0);
            _ = ffmpeg.avcodec_parameters_to_context(in_codec_ctx, ifmt_ctx->streams[stream_index]->codecpar);

            if (ffmpeg.avcodec_open2(in_codec_ctx, in_codec, &codec_options) != 0)
            {
                Debug.WriteLine("cannot initialize video decoder!");
                return;
            }

            if (ffmpeg.avformat_alloc_output_context2(&ofmt_ctx, null, "flv", null) != 0)
            {
                Debug.WriteLine("cannot initialize output format context!\n");
                return;
            }
            if (ffmpeg.avio_open2(&ofmt_ctx->pb, url, ffmpeg.AVIO_FLAG_WRITE, null, null) != 0)
            {
                Debug.WriteLine("could not open IO context!\n");
                return;
            }

            out_codec = ffmpeg.avcodec_find_encoder(AVCodecID.AV_CODEC_ID_H264);
            out_stream = ffmpeg.avformat_new_stream(ofmt_ctx, out_codec);
            out_codec_ctx = ffmpeg.avcodec_alloc_context3(out_codec);
            AVRational dst_fps = new() { den = 1, num = 30 };

            out_codec_ctx->codec_tag = 0;
            out_codec_ctx->codec_id = AVCodecID.AV_CODEC_ID_H264;
            out_codec_ctx->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
            out_codec_ctx->width = width;
            out_codec_ctx->height = height;
            out_codec_ctx->gop_size = 1;
            out_codec_ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            out_codec_ctx->framerate = dst_fps;
            out_codec_ctx->time_base = ffmpeg.av_inv_q(dst_fps);
            if (ffmpeg.avcodec_parameters_from_context(out_stream->codecpar, out_codec_ctx) != 0)
            {
                Debug.WriteLine("could not initialize stream codec parameters!\n");
                return;
            }

            AVDictionary* _codec_options = null;
            _ = ffmpeg.av_dict_set(&_codec_options, "profile", "baseline", 0);
            _ = ffmpeg.av_dict_set(&_codec_options, "preset", "ultrafast", 0);
            _ = ffmpeg.av_dict_set(&_codec_options, "tune", "zerolatency", 0);
            _ = ffmpeg.av_dict_set(&codec_options, "framerate", "30", 0);
            if (ffmpeg.avcodec_open2(out_codec_ctx, out_codec, &codec_options) != 0)
            {
                Debug.WriteLine("could not open video encoder!\n");
                return;
            }
            out_stream->codecpar->extradata = out_codec_ctx->extradata;
            out_stream->codecpar->extradata_size = out_codec_ctx->extradata_size;
            if (ffmpeg.avformat_write_header(ofmt_ctx, null) != 0)
            {
                Debug.WriteLine("could not write header to ouput context!\n");
                return;
            }

            AVFrame* frame = ffmpeg.av_frame_alloc();
            AVFrame* outframe = ffmpeg.av_frame_alloc();
            AVPacket* pkt = ffmpeg.av_packet_alloc();
            byte_ptrArray4 dstData = new();
            int_array4 dstLinesize = new();

            int nbytes = ffmpeg.av_image_get_buffer_size(out_codec_ctx->pix_fmt, out_codec_ctx->width, out_codec_ctx->height, 32);
            byte* video_outbuf = (byte*)ffmpeg.av_malloc((ulong)nbytes);
            _ = ffmpeg.av_image_fill_arrays(ref dstData, ref dstLinesize, video_outbuf, AVPixelFormat.AV_PIX_FMT_YUV420P, out_codec_ctx->width, out_codec_ctx->height, 1);
            outframe->width = width;
            outframe->height = height;
            outframe->format = (int)out_codec_ctx->pix_fmt;
            SwsContext* swsctx = ffmpeg.sws_getContext(in_codec_ctx->width, in_codec_ctx->height, in_codec_ctx->pix_fmt,
                                                        out_codec_ctx->width, out_codec_ctx->height, out_codec_ctx->pix_fmt,
                                                        ffmpeg.SWS_BICUBIC, null, null, null);
            _ = ffmpeg.av_new_packet(pkt, 0);
            long pts = 0;
            int ret;
            int y_size = out_codec_ctx->width * out_codec_ctx->height;
           
            while (true)
            {
                if (ffmpeg.av_read_frame(ifmt_ctx, pkt) < 0)
                {
                    continue;
                }

                frame = ffmpeg.av_frame_alloc();
                if (ffmpeg.avcodec_send_packet(in_codec_ctx, pkt) != 0)
                {
                    Debug.WriteLine("error sending packet to input codec context!\n");
                    break;
                }

                if (ffmpeg.avcodec_receive_frame(in_codec_ctx, frame) != 0)
                {
                    Debug.WriteLine("error receiving frame from input codec context!\n");
                    break;
                }

                ffmpeg.av_packet_unref(pkt);
                _ = ffmpeg.av_new_packet(pkt, 0);

                _ = ffmpeg.sws_scale(swsctx, frame->data, frame->linesize, 0, in_codec_ctx->height, dstData, dstLinesize);
                ffmpeg.av_frame_free(&frame);


                outframe->data[0] = dstData[0];
                outframe->data[1] = dstData[0] + y_size;
                outframe->data[2] = dstData[0] + (y_size * 5 / 4);
                outframe->linesize[0] = dstLinesize[0];
                outframe->linesize[1] = dstLinesize[1];
                outframe->linesize[2] = dstLinesize[2];
                outframe->pts = pts++;

               
                if (texture == IntPtr.Zero || renderer == IntPtr.Zero)
                {
                    renderer = SDL.SDL_CreateRenderer(window, -1, SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED);
                    texture = SDL.SDL_CreateTexture(renderer, SDL.SDL_PIXELFORMAT_IYUV, (int)SDL.SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING, width, height);
                    rect = new SDL.SDL_Rect() { x = 0, y = 0, w = width, h = height };
                    SDL.SDL_SetWindowSize(window, width, height);
                    _ = SDL.SDL_RenderSetLogicalSize(renderer, width, height);
                }               
                int yPitch = dstLinesize[0];
                int uPitch = dstLinesize[1];
                int vPitch = dstLinesize[2];
                IntPtr y = (IntPtr)outframe->data[0];
                IntPtr u = (IntPtr)outframe->data[1];
                IntPtr v = (IntPtr)outframe->data[2];
                ret = SDL.SDL_UpdateYUVTexture(texture, ref rect, y, yPitch, u, uPitch, v, vPitch);
                _ = SDL.SDL_RenderCopy(renderer, texture, IntPtr.Zero, ref rect);
                SDL.SDL_RenderPresent(renderer);
                
               

                if (outframe->pict_type == AVPictureType.AV_PICTURE_TYPE_B)
                {
                    continue;
                }
                if (ffmpeg.avcodec_send_frame(out_codec_ctx, outframe) != 0)
                {
                    Debug.WriteLine("error sending frame to output codec context!\n");
                    break;
                }

                if (ffmpeg.avcodec_receive_packet(out_codec_ctx, pkt) != 0)
                {
                   
                    Debug.WriteLine("error receiving packet from output codec context!\n");
                }
                else
                {
                    pkt->pts = ffmpeg.av_rescale_q(pkt->pts, out_codec_ctx->time_base, out_stream->time_base);
                    pkt->dts = ffmpeg.av_rescale_q(pkt->dts, out_codec_ctx->time_base, out_stream->time_base);
                    _ = ffmpeg.av_interleaved_write_frame(ofmt_ctx, pkt);
                    ffmpeg.av_packet_unref(pkt);
                    _ = ffmpeg.av_new_packet(pkt, 0);
                }
            }

            _ = ffmpeg.av_write_trailer(ofmt_ctx);
            ffmpeg.av_frame_free(&outframe);
            _ = ffmpeg.avio_close(ofmt_ctx->pb);
            ffmpeg.avformat_free_context(ofmt_ctx);
            _ = ffmpeg.avio_close(ifmt_ctx->pb);
            ffmpeg.avformat_free_context(ifmt_ctx);
            SDL.SDL_DestroyRenderer(renderer);
            SDL.SDL_DestroyTexture(texture);
            SDL.SDL_DestroyWindow(window);
            return;
        }
    }
}
