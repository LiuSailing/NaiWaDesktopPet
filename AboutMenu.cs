using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NaiwaDesktopPet
{
    internal sealed class AboutMenuItem : ToolStripMenuItem
    {
        internal const string AuthorName = "LiuSailing";
        internal const string RepositoryUrl = "https://github.com/LiuSailing/NaiWaDesktopPet";
        readonly AboutContent content;
        readonly ToolStripControlHost host;

        public AboutMenuItem(float scale, Font menuFont, string assetDirectory, Action closeMenu) : base("关于")
        {
            content = new AboutContent(scale, menuFont, assetDirectory, () => {
                closeMenu();
                try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
                catch (Win32Exception) { ShowLinkError(); }
                catch (InvalidOperationException) { ShowLinkError(); }
            });
            host = new ToolStripControlHost(content) {
                AutoSize = false, Size = content.Size, Margin = Padding.Empty, Padding = Padding.Empty
            };
            DropDown = new ToolStripDropDownMenu {
                Font = menuFont, AutoClose = true, AutoSize = false, CanOverflow = false,
                ShowImageMargin = true, ShowCheckMargin = false, Margin = Padding.Empty,
                AccessibleName = "关于奶蛙桌宠"
            };
            DropDown.Items.Add(host);
            DropDown.Size = new Size(content.Width + DropDown.Padding.Horizontal,
                content.Height + DropDown.Padding.Vertical);
            DropDownOpening += (s, e) => {
                if (Owner == null) return;
                int index = Owner.Items.IndexOf(this), bottom = Bounds.Bottom;
                if (index + 1 < Owner.Items.Count && Owner.Items[index + 1].Available)
                    bottom = Owner.Items[index + 1].Bounds.Bottom;
                content.FitHeight(bottom - Bounds.Top - DropDown.Padding.Vertical);
                host.Size = content.Size;
                DropDown.Size = new Size(content.Width + DropDown.Padding.Horizontal,
                    content.Height + DropDown.Padding.Vertical);
            };
        }

        protected override Point DropDownLocation
        {
            get
            {
                if (Owner == null) return base.DropDownLocation;
                int top = Owner.PointToScreen(Bounds.Location).Y;
                var anchor = new Rectangle(Owner.Bounds.Left, top, Owner.Width, Bounds.Height);
                return SoundOptionsMenuItem.AdjacentLocation(anchor, DropDown.Size,
                    Screen.FromRectangle(Owner.Bounds).WorkingArea);
            }
        }

        static void ShowLinkError()
        {
            MessageBox.Show("无法打开浏览器，请复制下面的地址访问项目：" + Environment.NewLine + RepositoryUrl,
                "奶蛙桌宠", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        sealed class AboutContent : Panel
        {
            readonly PictureBox github;
            readonly ToolTip tooltip = new ToolTip();
            readonly Label title;
            readonly LinkLabel author;
            readonly int padding, gap;

            public AboutContent(float scale, Font menuFont, string assetDirectory, Action openRepository)
            {
                Font = menuFont; BackColor = SystemColors.Window; ForeColor = SystemColors.MenuText;
                AccessibleName = "关于奶蛙桌宠"; AccessibleRole = AccessibleRole.Pane;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                padding = (int)Math.Round(10 * scale); gap = (int)Math.Round(6 * scale);
                int iconSize = (int)Math.Round(16 * scale);
                title = new Label {
                    Text = "奶蛙桌宠 · NaiWaDesktopPet", AutoSize = true,
                    Font = menuFont, TextAlign = ContentAlignment.MiddleLeft
                };
                author = new LinkLabel {
                    Text = "作者：" + AuthorName, AutoSize = true, Font = menuFont,
                    LinkArea = new LinkArea(3, AuthorName.Length), LinkBehavior = LinkBehavior.AlwaysUnderline,
                    LinkColor = Color.FromArgb(0, 102, 204), VisitedLinkColor = Color.FromArgb(0, 102, 204),
                    ActiveLinkColor = Color.FromArgb(0, 70, 140), TabStop = true,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AccessibleName = "作者：" + AuthorName, AccessibleDescription = "打开奶蛙桌宠 GitHub 仓库"
                };
                author.LinkClicked += (s, e) => openRepository();
                tooltip.SetToolTip(author, "打开奶蛙桌宠 GitHub 仓库");
                github = new PictureBox {
                    Size = new Size(iconSize, iconSize), SizeMode = PictureBoxSizeMode.Zoom,
                    AccessibleName = "GitHub", AccessibleRole = AccessibleRole.Graphic, TabStop = false
                };
                string iconPath = Path.Combine(assetDirectory, "..", "github", "github-mark.png");
                if (File.Exists(iconPath))
                    using (var source = Image.FromFile(iconPath)) github.Image = new Bitmap(source);
                Controls.Add(title); Controls.Add(github); Controls.Add(author);
                Width = Math.Max(title.PreferredSize.Width, iconSize + gap + author.PreferredSize.Width) + padding * 2;
                title.AutoSize = false; author.AutoSize = false;
                title.Width = Width - padding * 2;
                author.Width = Width - padding * 2 - iconSize - gap;
                FitHeight(Font.Height + Math.Max(iconSize, Font.Height) + 4);
            }

            public void FitHeight(int height)
            {
                Height = Math.Max(height, Font.Height + Math.Max(github.Height, Font.Height));
                int firstRow = Height / 2;
                int secondRow = Height - firstRow;
                title.Height = firstRow; author.Height = secondRow;
                title.Location = new Point(padding, 0);
                github.Location = new Point(padding, firstRow + (secondRow - github.Height) / 2);
                author.Location = new Point(padding + github.Width + gap, firstRow);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    tooltip.Dispose();
                    if (github.Image != null) { github.Image.Dispose(); github.Image = null; }
                }
                base.Dispose(disposing);
            }
        }
    }
}
