﻿using ScreenShare.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ScreenShare
{
    public partial class ScreenShare : Form
    {
        /********** 配置参数 **********/
        /// <summary>
        /// 已启动
        /// </summary>
        private bool isWorking = false;
        /// <summary>
        /// 分享地址
        /// </summary>
        private string shareLink;
        /// <summary>
        /// 开启加密
        /// </summary>
        private bool isEncryption;
        /// <summary>
        /// 账号
        /// </summary>
        private string account;
        /// <summary>
        /// 密码
        /// </summary>
        private string pwd;
        /// <summary>
        /// 显示器
        /// </summary>
        private Rectangle screen;
        /// <summary>
        /// 视频
        /// </summary>
        private Size video;
        /// <summary>
        /// 显示光标
        /// </summary>
        private bool isDisplayCursor;
        /// <summary>
        /// 每秒帧数
        /// </summary>
        private int videoFrame;
        /// <summary>
        /// 视频质量
        /// </summary>
        private int videoQuality;
        /// <summary>
        /// 网页内容
        /// </summary>
        private string html;
        /// <summary>
        /// 账号密码
        /// </summary>
        private string auth;

        /********** 其他参数 **********/
        /// <summary>
        /// IP地址列表
        /// </summary>
        private List<Tuple<string, string>> ipList;
        /// <summary>
        /// 屏幕信息列表
        /// </summary>
        private List<Tuple<string, Rectangle>> screenList;
        /// <summary>
        /// 图标MemoryStream
        /// </summary>
        private readonly MemoryStream faviconStream = new MemoryStream();
        /// <summary>
        /// 图片MemoryStream
        /// </summary>
        private readonly MemoryStream imageStream = new MemoryStream();
        /// <summary>
        /// 服务器
        /// </summary>
        private HttpListener server = new HttpListener();
        /// <summary>
        /// 接收和返回上下文
        /// </summary>
        private HttpListenerContext ctx;
        /// <summary>
        /// 返回结果
        /// </summary>
        private byte[] responseData;

        /********** 主方法 **********/
        /// <summary>
        /// 启动
        /// </summary>
        public ScreenShare()
        {
            InitializeComponent();
            // 初始化
            Init();
            // 读取图标
            Resources.favicon.Save(faviconStream);
            Log("屏幕共享初始化完成！");
        }

        /********** 私有方法 **********/
        /// <summary>
        /// 初始化
        /// </summary>
        private void Init()
        {
            /* 头部 */
            // IP地址
            ipList = Utils.GetAllIPv4Address();
            ipAddressComboBox.Items.Clear();
            foreach (var ip in ipList)
            {
                ipAddressComboBox.Items.Add(ip.Item2 + " - " + ip.Item1);
            }
            ipAddressComboBox.SelectedIndex = 0;
            // 端口号
            ipPortNud.Value = 7070;
            // 分享地址
            shareLinkText.Text = "http://" + ipList.ElementAt(ipAddressComboBox.SelectedIndex).Item2 + ":" + ipPortNud.Value + "/";

            /* 加密传输 */
            // 开启加密
            isEncryptionCb.Checked = false;
            // 账号
            accountText.Text = "";
            // 密码
            pwdText.Text = "";

            /* 选取位置 */
            // 全屏
            isFullScreenCb.Checked = true;
            // 显示器
            screenList = Utils.GetAllScreen();
            screenComboBox.Items.Clear();
            foreach (var screen in screenList)
            {
                screenComboBox.Items.Add(screen.Item1 + "[" + screen.Item2.Width + "x" + screen.Item2.Height + "]");
            }
            if (screenList.Count == 1)
            {
                screenComboBox.SelectedIndex = 0;
            }
            else
            {
                screenComboBox.SelectedIndex = 1;
            }
            Rectangle selectedScreen = screenList.ElementAt(screenComboBox.SelectedIndex).Item2;
            // X
            screenXNud.Minimum = selectedScreen.Left;
            screenXNud.Maximum = selectedScreen.Right - 2;
            screenXNud.Value = selectedScreen.X;
            // Y
            screenYNud.Minimum = selectedScreen.Top;
            screenYNud.Maximum = selectedScreen.Bottom - 2;
            screenYNud.Value = selectedScreen.Y;
            // 宽
            screenWNud.Maximum = selectedScreen.Width;
            screenWNud.Value = selectedScreen.Width;
            // 高
            screenHNud.Maximum = selectedScreen.Height;
            screenHNud.Value = selectedScreen.Height;

            /* 视频尺寸 */
            // 锁定缩放比
            isLockAspectRatioCb.Checked = true;
            // 缩放比例
            scalingNud.Value = 100;
            // 宽
            videoWNud.Value = selectedScreen.Width;
            // 高
            screenHNud.Value = selectedScreen.Height;

            /* 视频设置 */
            // 显示光标
            isDisplayCursorCb.Checked = true;
            // 每秒帧数
            videoFrameNud.Value = 5;
            // 视频质量
            videoQualityNud.Value = 100;
        }

        /// <summary>
        /// 加载配置
        /// </summary>
        private void LoadConfig()
        {
            shareLink = shareLinkText.Text;
            isEncryption = isEncryptionCb.Checked;
            account = accountText.Text;
            pwd = pwdText.Text;
            screen = new Rectangle((int)screenXNud.Value, (int)screenYNud.Value, (int)screenWNud.Value, (int)screenHNud.Value);
            video = new Size((int)videoWNud.Value, (int)videoHNud.Value);
            isDisplayCursor = isDisplayCursorCb.Checked;
            videoFrame = (int)videoFrameNud.Value;
            videoQuality = (int)videoQualityNud.Value;
            html = Resources.Html1 + (1000 / videoFrame) + Resources.Html2;
            auth = account + ":" + pwd;
        }

        /// <summary>
        /// 开启HTTP服务器
        /// </summary>
        private async void StartServerTask()
        {
            while (isWorking)
            {
                try
                {
                    ctx = await server.GetContextAsync();
                    // 开启加密
                    if (isEncryption)
                    {
                        // 未输入账号密码
                        if (!ctx.Request.Headers.AllKeys.Contains("Authorization"))
                        {
                            ctx.Response.StatusCode = 401;
                            ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=ScreenShare Authentication");
                            ctx.Response.Close();
                            continue;
                        }
                        // 已输入账号密码
                        else
                        {
                            // 获取到输入的账号密码
                            string auth = ctx.Request.Headers["Authorization"];
                            // 移除头部"Basic "字符串
                            auth = auth.Remove(0, 6);
                            // 解码账号密码
                            auth = Encoding.UTF8.GetString(Convert.FromBase64String(auth));
                            // 账号密码错误
                            if (auth != this.auth)
                            {
                                responseData = Encoding.UTF8.GetBytes("<h1 style=\"color:red\">Not Authorized !");
                                ctx.Response.ContentType = "text/html";
                                ctx.Response.StatusCode = 401;
                                ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=ScreenShare Authentication");
                                await ctx.Response.OutputStream.WriteAsync(responseData, 0, responseData.Length);
                                ctx.Response.Close();
                                continue;
                            }
                        }
                    }
                    // 请求成功
                    ctx.Response.StatusCode = 200;
                    // 判断请求地址
                    switch (ctx.Request.Url.LocalPath)
                    {
                        // 图标
                        case "/favicon.ico":
                            {
                                ctx.Response.ContentType = "image/x-icon";
                                responseData = faviconStream.ToArray();
                                break;
                            }
                        // 图片
                        case "/image.jpg":
                            {
                                ctx.Response.ContentType = "image/jpeg";
                                responseData = imageStream.ToArray();
                                break;
                            }
                        // 网页
                        default:
                            {
                                ctx.Response.ContentType = "text/html;charset=UTF-8";
                                responseData = Encoding.UTF8.GetBytes(html);
                                break;
                            }
                    }
                    await ctx.Response.OutputStream.WriteAsync(responseData, 0, responseData.Length);
                    ctx.Response.Close();
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>
        /// 开启屏幕捕获
        /// </summary>
        private async void CaptureScreenTask()
        {
            previewImg.Image = new Bitmap(faviconStream);
            // 判断捕获图片属性
            // 普通
            if (screen.Size == video && videoQuality == 100)
            {
                while (isWorking)
                {
                    try
                    {
                        imageStream.SetLength(0);
                        ImageUtils.Save(ImageUtils.CaptureScreenArea(screen, isDisplayCursor), imageStream);
                        previewImg.Image.Dispose();
                        previewImg.Image = new Bitmap(imageStream);
                        await Task.Delay(1000 / videoFrame);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            // 缩放
            else if (screen.Size != video && videoQuality == 100)
            {
                while (isWorking)
                {
                    try
                    {
                        imageStream.SetLength(0);
                        ImageUtils.Save(ImageUtils.ZoomImage(ImageUtils.CaptureScreenArea(screen, isDisplayCursor), video), imageStream);
                        previewImg.Image.Dispose();
                        previewImg.Image = new Bitmap(imageStream);
                        await Task.Delay(1000 / videoFrame);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            // 压缩
            else if (screen.Size == video && videoQuality != 100)
            {
                while (isWorking)
                {
                    try
                    {
                        imageStream.SetLength(0);
                        ImageUtils.QualitySave(ImageUtils.CaptureScreenArea(screen, isDisplayCursor), videoQuality, imageStream);
                        previewImg.Image.Dispose();
                        previewImg.Image = new Bitmap(imageStream);
                        await Task.Delay(1000 / videoFrame);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            // 缩放+压缩
            else
            {
                while (isWorking)
                {
                    try
                    {
                        imageStream.SetLength(0);
                        ImageUtils.QualitySave(ImageUtils.ZoomImage(ImageUtils.CaptureScreenArea(screen, isDisplayCursor), video), videoQuality, imageStream);
                        previewImg.Image.Dispose();
                        previewImg.Image = new Bitmap(imageStream);
                        await Task.Delay(1000 / videoFrame);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// 写日志
        /// </summary>
        /// <param name="text">内容</param>
        private void Log(string text)
        {
            logText.Text += DateTime.Now.ToLongTimeString() + " : " + text + "\r\n";
            logText.SelectionStart = logText.Text.Length;
            logText.ScrollToCaret();
        }

        /********** 界面事件 **********/
        /// <summary>
        /// 点击开始共享按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartSharingScreenBtn_Click(object sender, EventArgs e)
        {
            if (isWorking)
            {
                isWorking = false;
                Log("屏幕共享已停止。");
                startSharingScreenBtn.Text = "开始共享";
                server.Stop();
                // 手动gc
                GC.Collect();
            }
            else
            {
                try
                {
                    // 加载配置
                    LoadConfig();
                    server.Prefixes.Clear();
                    server.Prefixes.Add(shareLink);
                    server.Start();
                    isWorking = true;
                    // 开启屏幕捕获
                    CaptureScreenTask();
                    // 开启HTTP服务器
                    StartServerTask();
                    Log("屏幕共享已开启。");
                    startSharingScreenBtn.Text = "停止共享";
                }
                catch (Exception)
                {
                    MessageBox.Show("启动失败，可能是端口号冲突，请更换端口号！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    // 报错后Prefixes会被清空
                    server = new HttpListener();
                }
            }
        }

        /// <summary>
        /// IP地址改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IpAddressComboBox_SelectedValueChanged(object sender, EventArgs e)
        {
            shareLinkText.Text = "http://" + ipList.ElementAt(ipAddressComboBox.SelectedIndex).Item2 + ":" + ipPortNud.Value + "/";
        }

        /// <summary>
        /// IP端口号改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IpPortNud_ValueChanged(object sender, EventArgs e)
        {
            shareLinkText.Text = "http://" + ipList.ElementAt(ipAddressComboBox.SelectedIndex).Item2 + ":" + ipPortNud.Value + "/";
        }

        /// <summary>
        /// 点击重新加载配置按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ReloadConfigBtn_Click(object sender, EventArgs e)
        {
            Init();
        }

        /// <summary>
        /// 点击关于按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AboutBtn_Click(object sender, EventArgs e)
        {
            new About().ShowDialog();
        }

        /// <summary>
        /// 点击复制按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CopyBtn_Click(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(shareLinkText.Text);
                Log("分享链接已复制。");
            }
            catch (Exception)
            {
                MessageBox.Show("复制失败，请手动复制！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 点击用浏览器打开按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenBtn_Click(object sender, EventArgs e)
        {
            Process.Start(shareLink);
        }

        /// <summary>
        /// 开启密码CheckBox状态改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IsEncryptionCb_CheckStateChanged(object sender, EventArgs e)
        {
            accountText.Enabled = pwdText.Enabled = ((CheckBox)sender).Checked;
        }

        /// <summary>
        /// 全屏CheckBox状态改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IsFullScreenCb_CheckStateChanged(object sender, EventArgs e)
        {
            bool isChecked = ((CheckBox)sender).Checked;
            screenXNud.Enabled = screenYNud.Enabled = screenWNud.Enabled = screenHNud.Enabled = !isChecked;
            if (isChecked)
            {
                Rectangle selectedScreen = screenList.ElementAt(screenComboBox.SelectedIndex).Item2;
                screenXNud.Value = selectedScreen.X;
                screenYNud.Value = selectedScreen.Y;
                screenWNud.Value = selectedScreen.Width;
                screenHNud.Value = selectedScreen.Height;
            }
        }

        /// <summary>
        /// 显示器改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScreenComboBox_SelectedValueChanged(object sender, EventArgs e)
        {
            Rectangle selectedScreen = screenList.ElementAt(screenComboBox.SelectedIndex).Item2;
            screenXNud.Minimum = selectedScreen.Left;
            screenXNud.Maximum = selectedScreen.Right - 2;
            screenXNud.Value = selectedScreen.X;
            screenYNud.Minimum = selectedScreen.Top;
            screenYNud.Maximum = selectedScreen.Bottom - 2;
            screenYNud.Value = selectedScreen.Y;
            screenWNud.Maximum = selectedScreen.Width;
            screenWNud.Value = selectedScreen.Width;
            screenHNud.Maximum = selectedScreen.Height;
            screenHNud.Value = selectedScreen.Height;
        }

        /// <summary>
        /// 屏幕的X发生改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScreenXNud_ValueChanged(object sender, EventArgs e)
        {
            Rectangle selectedScreen = screenList.ElementAt(screenComboBox.SelectedIndex).Item2;
            screenWNud.Maximum = selectedScreen.Right - screenXNud.Value;
        }

        /// <summary>
        /// 屏幕的Y发生改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScreenYNud_ValueChanged(object sender, EventArgs e)
        {
            Rectangle selectedScreen = screenList.ElementAt(screenComboBox.SelectedIndex).Item2;
            screenHNud.Maximum = selectedScreen.Bottom - screenYNud.Value;
        }

        /// <summary>
        /// 屏幕的宽发生改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScreenWNud_ValueChanged(object sender, EventArgs e)
        {
            videoWNud.Value = screenWNud.Value * scalingNud.Value / 100;
        }

        /// <summary>
        /// 屏幕的高发生改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScreenHNud_ValueChanged(object sender, EventArgs e)
        {
            videoHNud.Value = screenHNud.Value * scalingNud.Value / 100;
        }

        /// <summary>
        /// 点击选取屏幕坐标按钮
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void CaptureScreenCoordinatesBtn_Click(object sender, EventArgs e)
        {
            DrawScreen drawScreen = new DrawScreen(screenList.ElementAt(screenComboBox.SelectedIndex).Item2);
            if (drawScreen.ShowDialog() == DialogResult.OK)
            {
                Rectangle rect = drawScreen.rect;
                if (rect.Width != 0 && rect.Height != 0)
                {
                    isFullScreenCb.Checked = false;
                    screenXNud.Value = rect.X;
                    screenYNud.Value = rect.Y;
                    screenWNud.Value = rect.Width;
                    screenHNud.Value = rect.Height;
                }
            }
        }

        /// <summary>
        /// 锁定纵横比CheckBox状态改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void IsLockAspectRatioCb_CheckStateChanged(object sender, EventArgs e)
        {
            bool isChecked = ((CheckBox)sender).Checked;
            scalingNud.Enabled = isChecked;
            videoWNud.Enabled = videoHNud.Enabled = !isChecked;
            if (isChecked)
            {
                videoWNud.Value = screenWNud.Value * scalingNud.Value / 100;
                videoHNud.Value = screenHNud.Value * scalingNud.Value / 100;
            }
        }

        /// <summary>
        /// 视频的缩放比例发生改变
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ScalingNud_ValueChanged(object sender, EventArgs e)
        {
            videoWNud.Value = screenWNud.Value * scalingNud.Value / 100;
            videoHNud.Value = screenHNud.Value * scalingNud.Value / 100;
        }

        /// <summary>
        /// 点击图片，全窗口展示
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PreviewImg_Click(object sender, EventArgs e)
        {
            if (previewImg.Dock == DockStyle.None)
            {
                previewImg.Dock = DockStyle.Fill;
            }
            else
            {
                previewImg.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            }
        }

    }
}
