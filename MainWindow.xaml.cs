using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace SmartDropZone
{
    /// <summary>What kind of payload a shelf item carries.</summary>
    public enum ShelfItemKind { File, Folder, Url, Snippet }

    /// <summary>
    /// A single pinned card on the shelf. Only the plain fields are persisted
    /// (the <see cref="Icon"/> preview is regenerated at startup).
    /// </summary>
    public sealed class ShelfItem
    {
        public ShelfItemKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }      // file or folder
        public string? Url { get; set; }       // web link
        public string? Text { get; set; }      // text snippet
        public string? Extension { get; set; } // e.g. "PNG"
        public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;

        /// <summary>Folders sort ahead of everything else, like Explorer.</summary>
        [JsonIgnore]
        public bool IsFolder => Kind == ShelfItemKind.Folder;

        [JsonIgnore]
        public ImageSource? Icon { get; set; }

        [JsonIgnore]
        public string Subtitle => Kind switch
        {
            ShelfItemKind.File     => string.IsNullOrEmpty(Extension) ? "File" : $"{Extension} file",
            ShelfItemKind.Folder   => "Folder",
            ShelfItemKind.Url      => UrlHost,
            ShelfItemKind.Snippet  => $"{LineCount} line{(LineCount == 1 ? "" : "s")}",
            _                      => ""
        };

        [JsonIgnore]
        public string FullDescription => Kind switch
        {
            ShelfItemKind.File or ShelfItemKind.Folder => Path ?? "",
            ShelfItemKind.Url    => Url ?? "",
            ShelfItemKind.Snippet => Truncate(Text, 160),
            _                     => ""
        };

        private string UrlHost
        {
            get
            {
                if (Uri.TryCreate(Url, UriKind.Absolute, out var u)) return u.Host;
                return Url ?? "";
            }
        }

        private int LineCount => Text is null ? 0 : Text.Split('\n').Length;

        private static string Truncate(string? s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length <= max ? s : s[..max] + "…";
        }
    }

    /// <summary>Which screen edge the shelf is anchored to.</summary>
    public enum DockEdge { Right, Left, Top, Free }

    /// <summary>
    /// Main window: a borderless, top-most, screen-edge slide-out shelf.
    /// </summary>
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------
        // Win32 declarations
        // ------------------------------------------------------------------
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080; // hide from taskbar & Alt+Tab
        private const int WS_EX_TRANSPARENT = 0x00000020; // mouse clicks pass through
        private const int WS_EX_NOACTIVATE = 0x08000000;  // never steals focus

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static int GetWindowLong(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? (int)GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);

        private static void SetWindowLong(IntPtr hWnd, int nIndex, int value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, new IntPtr(value));
            else SetWindowLong32(hWnd, nIndex, value);
        }

        // SHGetFileInfo — used to render real Explorer file/folder icons.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_LARGEICON = 0x000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x010;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential)]
        private struct WinPoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WinRect { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out WinPoint lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out WinRect lpRect);

        // ------------------------------------------------------------------
        // Sizing / docking constants
        // ------------------------------------------------------------------
        private const double ShelfWidth = 320;
        private const double HandleWidth = 14;
        private const double TopShelfHeight = 200;
        private const double CapsuleW = 120;
        private const double CapsuleH = 36;

        private readonly ObservableCollection<ShelfItem> _items = new();
        private readonly DispatcherTimer _collapseTimer;
        private readonly DispatcherTimer _persistTimer;
        private readonly ListCollectionView _view;
        private SortMode _sortMode = SortMode.Name;

        /// <summary>Public binding surface for the card ListBox.</summary>
        public ObservableCollection<ShelfItem> Items => _items;

        /// <summary>Sortable view of the items shown in the list.</summary>
        public ICollectionView ItemsView => _view;

        private AppSettings _settings = new();
        private WinForms.NotifyIcon? _notifyIcon;
        private SettingsWindow? _settingsWindow;

        private DockEdge _dock = DockEdge.Right;
        private bool _isExpanded = true;
        private bool _isDraggingOut;
        private bool _capsuleDragging;
        private bool _capsuleMoved;
        private Point _capsuleDragStart;
        private double? _capsuleL;
        private double? _capsuleT;
        private bool _pinned;
        private bool _animating;
        private DispatcherTimer? _animTimer;
        private DispatcherTimer? _hoverTimer; // drives hover expand/collapse
        private DateTime? _overLostAt;        // when the cursor last left the window
        private ScaleTransform? _shelfScale;  // condenses the shelf into the capsule
        private double _dockedVerticalOffset = double.NaN;  // user's custom Top for right/left dock
        private double _dockedHorizontalOffset = double.NaN; // user's custom Left for top dock

        // Header drag (manual, replaces DragMove so we can show the hold ring)
        private const double OutOfDockThreshold = 24;
        private const double SnapThreshold = 30;
        private const double HoldMoveReset = 40;   // must move ~40px to restart the idle + fill
        private bool _headerDragging;
        private bool _holdIsDock;   // true = holding near an edge to dock; false = holding out to detach
        private DockEdge? _holdTargetEdge; // when switching docks directly
        private Point _lastHeaderScreen;
        private Point _holdBaseScreen;
        private DateTime? _holdStartedAt;
        private DispatcherTimer? _holdTimer;
        private Window? _snapPreview; // translucent overlay showing the dock target

        // Slide-out positions. Docked shelves animate one axis (Left or Top);
        // the free-floating shelf animates both and shrinks to a capsule pill.
        private double _expandedPosL;
        private double _expandedPosT;
        private double _collapsedPosL;
        private double _collapsedPosT;

        // Expanded shelf geometry while free-floating (restored when expanding).
        private double _freeL;
        private double _freeT;
        private double _freeW = 320;
        private double _freeH = 560;

        // Drag-out bookkeeping
        private Point _dragStart;
        private ShelfItem? _dragCandidate;

        private static readonly string[] ImageExtensions =
            { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tiff", ".webp", ".ico", ".jfif" };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static string PersistFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "SmartDropZone", "shelf.json");

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this; // bind the card ListBox to _items

            _collapseTimer = new DispatcherTimer();
            _collapseTimer.Tick += CollapseTimer_Tick;

            _persistTimer = new DispatcherTimer();
            _persistTimer.Tick += (_, _) => PersistFreeGeometry();

            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _holdTimer.Tick += HoldTimer_Tick;

            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _hoverTimer.Tick += HoverTimer_Tick;

            _view = new ListCollectionView(_items);

            LocationChanged += (_, _) => OnFreeGeometryChanged();
            SizeChanged += (_, _) => OnFreeGeometryChanged();
        }

        /// <summary>
        /// While the free-floating shelf is expanded, any move/resize keeps the
        /// stored free geometry (and thus the capsule's collapse spot) in sync.
        /// </summary>
        private void OnFreeGeometryChanged()
        {
            if (_dock != DockEdge.Free || !_isExpanded) return;
            if (_animating) return; // mid animation
            _freeL = Left;
            _freeT = Top;
            _freeW = Width;
            _freeH = Height;
            _expandedPosL = Left;
            _expandedPosT = Top;
            UpdateCollapsedPositions();
            RestartPersist();
        }

        // ==================================================================
        // Startup / layout
        // ==================================================================

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _items.CollectionChanged += OnItemsChanged;
            LoadPersisted();
            _settings = AppSettings.Load();
            _capsuleL = _settings.FreeCapsuleLeft;
            _capsuleT = _settings.FreeCapsuleTop;
            if (_settings.DockOffset is double off)
            {
                _dockedVerticalOffset = off;
                _dockedHorizontalOffset = off;
            }
            ApplySettings();
            ApplySort(_settings.SortMode);
            ApplyView(_settings.ViewMode);
            _hoverTimer?.Start();
            CreateTrayIcon();
            UpdateBadgeAndEmptyState();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

            _snapPreview?.Close();
            _snapPreview = null;

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }

        /// <summary>Add WS_EX_TOOLWINDOW so the shelf never shows in the taskbar or Alt+Tab.</summary>
        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
        }

        /// <summary>
        /// Arrange the shelf/handle grid, resize grips and window geometry for the
        /// current dock edge, and pre-compute the expanded / collapsed positions.
        /// </summary>
        private void ApplyLayout()
        {
            Rect wa = SystemParameters.WorkArea;
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            StopAnimation();
            ResetShelfScale();

            if (_dock == DockEdge.Top)
            {
                // Horizontal shelf at the top edge: handle strip along the screen
                // edge (bottom row). The shelf row+column are flexible so resizing
                // the window resizes the shelf and keeps the handle at the edge.
                RowShelf.Height = new GridLength(1, GridUnitType.Star);
                RowHandle.Height = new GridLength(HandleWidth);
                ColShelf.Width = new GridLength(1, GridUnitType.Star);
                ColHandle.Width = new GridLength(0);

                ShelfBorder.SetValue(Grid.RowProperty, 0);
                ShelfBorder.SetValue(Grid.ColumnProperty, 0);
                HandleArea.SetValue(Grid.RowProperty, 1);
                HandleArea.SetValue(Grid.ColumnProperty, 0);

                Grabber.Width = 88;
                Grabber.Height = 4;

                HandleArea.Visibility = Visibility.Visible;
                Capsule.Visibility = Visibility.Collapsed;
                ShelfBorder.Visibility = Visibility.Visible;
                ResizeOverlay.Visibility = Visibility.Visible;

                Width = _settings.DockWidth is double dw
                    ? Math.Clamp(dw, 280, Math.Max(280, wa.Width - 40))
                    : ShelfWidth + HandleWidth;
                Height = _settings.DockHeight is double dh
                    ? Math.Clamp(dh, 200, Math.Max(200, wa.Height - 30))
                    : TopShelfHeight + HandleWidth;
            }
            else if (_dock == DockEdge.Right || _dock == DockEdge.Left)
            {
                // Vertical shelf on a side edge. The handle strip sits on the
                // screen-edge side of the shelf: for the LEFT dock that's the
                // right column (handle col1 peeks at the left edge when collapsed);
                // for the RIGHT dock that's the left column (handle col0 peeks at
                // the right edge when collapsed).
                bool right = _dock == DockEdge.Right;
                RowShelf.Height = new GridLength(1, GridUnitType.Star);
                RowHandle.Height = new GridLength(0);
                if (right)
                {
                    // col0 = handle (14px, peeks at the right screen edge when collapsed),
                    // col1 = shelf (star)
                    ColShelf.Width = new GridLength(HandleWidth);
                    ColHandle.Width = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    // col0 = shelf (star), col1 = handle (14px, peeks at the left edge)
                    ColShelf.Width = new GridLength(1, GridUnitType.Star);
                    ColHandle.Width = new GridLength(HandleWidth);
                }

                ShelfBorder.SetValue(Grid.RowProperty, 0);
                ShelfBorder.SetValue(Grid.ColumnProperty, right ? 1 : 0);
                HandleArea.SetValue(Grid.RowProperty, 0);
                HandleArea.SetValue(Grid.ColumnProperty, right ? 0 : 1);

                Grabber.Width = 4;
                Grabber.Height = 88;

                HandleArea.Visibility = Visibility.Visible;
                Capsule.Visibility = Visibility.Collapsed;
                ShelfBorder.Visibility = Visibility.Visible;
                ResizeOverlay.Visibility = Visibility.Visible;

                Height = _settings.DockHeight is double dh
                    ? Math.Clamp(dh, 200, Math.Max(200, wa.Height - 30))
                    : Math.Clamp(wa.Height * 0.62, 320, 900);
                Width = _settings.DockWidth is double dw
                    ? Math.Clamp(dw, 280, Math.Max(280, wa.Width - 40))
                    : ShelfWidth + HandleWidth;
            }
            else // Free: floating anywhere on screen
            {
                RowShelf.Height = new GridLength(1, GridUnitType.Star);
                RowHandle.Height = new GridLength(0);
                ColShelf.Width = new GridLength(1, GridUnitType.Star);
                ColHandle.Width = new GridLength(0);

                // Reset shelf/handle to the default grid cells (free mode doesn't
                // use the handle column, but the shelf must be in the star cell).
                ShelfBorder.SetValue(Grid.RowProperty, 0);
                ShelfBorder.SetValue(Grid.ColumnProperty, 0);
                HandleArea.SetValue(Grid.RowProperty, 0);
                HandleArea.SetValue(Grid.ColumnProperty, 1);

                HandleArea.Visibility = Visibility.Collapsed;
                ShelfBorder.Visibility = Visibility.Visible;
                ResizeOverlay.Visibility = Visibility.Visible;

                double w = _settings.FreeWidth is double fw && fw >= 280 ? fw
                         : Width >= 280 ? Width : ShelfWidth + HandleWidth;
                double h = _settings.FreeHeight is double fh && fh >= 200 ? fh
                         : Height >= 200 ? Height : 560;
                double l = _settings.FreeLeft is double fl ? fl : Left;
                double t = _settings.FreeTop is double ft ? ft : Top;

                l = Math.Clamp(l, wa.Left - w + 60, wa.Right - 60);
                t = Math.Clamp(t, wa.Top - h + 60, wa.Bottom - 60);

                _freeW = w;
                _freeH = h;
                Width = w;
                Height = h;
                Left = l;
                Top = t;
            }

            SetThumbsAll();

            // The corner resize handle should sit away from the screen edge so it's
            // easy to grab. For the right dock the shelf hugs the right edge, so the
            // handle moves to the bottom-left (and behaves as a bottom-left corner).
            bool rightDock = _dock == DockEdge.Right;
            CornerResize.HorizontalAlignment = rightDock ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            CornerResize.Cursor = rightDock ? Cursors.SizeNESW : Cursors.SizeNWSE;
            CornerResize.Tag = rightDock ? "BottomLeft" : "BottomRight";
            // Clear All margin: keep space for the corner handle when it's on the
            // right, but let it go flush to the edge when the handle is on the left.
            ClearAllButton.Margin = new Thickness(0, 0, rightDock ? 0 : 30, 0);

            Reanchor();
        }

        /// <summary>All eight resize grips are always available, in every dock mode.</summary>
        private void SetThumbsAll()
        {
            ResizeLeft.Visibility = Visibility.Visible;
            ResizeRight.Visibility = Visibility.Visible;
            ResizeTop.Visibility = Visibility.Visible;
            ResizeBottom.Visibility = Visibility.Visible;
            ResizeTopLeft.Visibility = Visibility.Visible;
            ResizeTopRight.Visibility = Visibility.Visible;
            ResizeBottomLeft.Visibility = Visibility.Visible;
            ResizeBottomRight.Visibility = Visibility.Visible;

            ResizeLeft.Margin = new Thickness(0);
            ResizeRight.Margin = new Thickness(0);
            ResizeTop.Margin = new Thickness(0);
            ResizeBottom.Margin = new Thickness(0);
            ResizeTopLeft.Margin = new Thickness(0);
            ResizeTopRight.Margin = new Thickness(0);
            ResizeBottomLeft.Margin = new Thickness(0);
            ResizeBottomRight.Margin = new Thickness(0);
        }

        /// <summary>
        /// In docked modes: pin the anchored edge to the screen edge after a size
        /// change. In free mode: the position is fully user-controlled.
        /// Also refreshes the expanded/collapsed positions.
        /// </summary>
        private void Reanchor()
        {
            Rect wa = SystemParameters.WorkArea;

            switch (_dock)
            {
                case DockEdge.Right:
                    Left = wa.Right - Width;
                    if (double.IsNaN(_dockedVerticalOffset))
                        _dockedVerticalOffset = wa.Bottom - Height - 12;
                    _dockedVerticalOffset = Math.Clamp(_dockedVerticalOffset, wa.Top, wa.Bottom - Height);
                    Top = _dockedVerticalOffset;
                    break;
                case DockEdge.Left:
                    Left = wa.Left;
                    if (double.IsNaN(_dockedVerticalOffset))
                        _dockedVerticalOffset = wa.Bottom - Height - 12;
                    _dockedVerticalOffset = Math.Clamp(_dockedVerticalOffset, wa.Top, wa.Bottom - Height);
                    Top = _dockedVerticalOffset;
                    break;
                case DockEdge.Top:
                    if (double.IsNaN(_dockedHorizontalOffset))
                        _dockedHorizontalOffset = wa.Left;
                    _dockedHorizontalOffset = Math.Clamp(_dockedHorizontalOffset, wa.Left, wa.Right - Width);
                    Left = _dockedHorizontalOffset;
                    Top = wa.Top;
                    break;
                case DockEdge.Free:
                    _freeL = Left;
                    _freeT = Top;
                    _freeW = Width;
                    _freeH = Height;
                    break;
            }

            _expandedPosL = _dock == DockEdge.Top ? Left
                          : _dock == DockEdge.Right ? wa.Right - Width
                          : _dock == DockEdge.Left ? wa.Left
                          : Left;
            _expandedPosT = _dock == DockEdge.Top ? wa.Top
                          : _dock == DockEdge.Free ? Top
                          : Top;

            UpdateCollapsedPositions();
        }

        /// <summary>
        /// Where the collapsed window lands: beside the screen edge for docked modes,
        /// or a centered capsule pill at the same spot for the free-floating shelf.
        /// </summary>
        private void UpdateCollapsedPositions()
        {
            Rect wa = SystemParameters.WorkArea;
            switch (_dock)
            {
                case DockEdge.Right:
                    _collapsedPosL = wa.Right - HandleWidth;
                    _collapsedPosT = Top;
                    break;
                case DockEdge.Left:
                    _collapsedPosL = wa.Left - (Width - HandleWidth);
                    _collapsedPosT = Top;
                    break;
                case DockEdge.Top:
                    _collapsedPosL = Left;
                    _collapsedPosT = wa.Top - (Height - HandleWidth);
                    break;
                case DockEdge.Free:
                {
                    // The capsule settles at the top-middle of the shelf, so
                    // expanding back covers the same spot the capsule was in.
                    double topMidL = Math.Clamp(Left + (Width - CapsuleW) / 2, wa.Left, wa.Right - CapsuleW);
                    double topMidT = Math.Clamp(Top, wa.Top, wa.Bottom - CapsuleH);

                    if (_settings.AutoOpenCapsule)
                    {
                        _collapsedPosL = topMidL;
                        _collapsedPosT = topMidT;
                    }
                    else
                    {
                        _collapsedPosL = Math.Clamp(_capsuleL ?? topMidL, wa.Left, wa.Right - CapsuleW);
                        _collapsedPosT = Math.Clamp(_capsuleT ?? topMidT, wa.Top, wa.Bottom - CapsuleH);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Standard window resize math: the dragged edge(s) move, the opposite edge
        /// stays fixed. Docked shelves keep their screen edge pinned during the drag
        /// (like resizing a snapped window), so they never drift across the screen.
        /// </summary>
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;

            Rect wa = SystemParameters.WorkArea;
            double oldW = Width;
            double oldH = Height;

            double w = oldW;
            double h = oldH;
            if (tag.Contains("Left"))   w -= e.HorizontalChange;
            if (tag.Contains("Right"))  w += e.HorizontalChange;
            if (tag.Contains("Top"))    h -= e.VerticalChange;
            if (tag.Contains("Bottom")) h += e.VerticalChange;

            double newW = Math.Clamp(w, 280, Math.Max(280, wa.Width - 40));
            double newH = Math.Clamp(h, 200, Math.Max(200, wa.Height - 30));

            Width = newW;
            Height = newH;

            // Move the dragged edges, then pin the docked screen edge.
            if (tag.Contains("Left")) Left += oldW - newW;
            if (tag.Contains("Top"))  Top += oldH - newH;

            switch (_dock)
            {
                case DockEdge.Right: Left = wa.Right - Width; break;
                case DockEdge.Left:  Left = wa.Left; break;
                case DockEdge.Top:   Top = wa.Top; break;
            }

            UpdateCollapsedPositions();
        }

        /// <summary>
        /// On release: docked shelves snap back to their screen edge (keeping the
        /// user's custom position along that edge); free shelves keep the size.
        /// </summary>
        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_dock == DockEdge.Free)
            {
                PersistFreeGeometry();
            }
            else
            {
                // Remember where the user slid it along the dock edge before snapping.
                if (_dock == DockEdge.Top) _dockedHorizontalOffset = Left;
                else _dockedVerticalOffset = Top;
                Reanchor();
                _settings.DockWidth = Width;
                _settings.DockHeight = Height;
                SaveDockOffset();
            }
        }

        /// <summary>Debounced save of the free-floating position/size.</summary>
        private void RestartPersist()
        {
            _persistTimer.Stop();
            _persistTimer.Interval = TimeSpan.FromMilliseconds(500);
            _persistTimer.Start();
        }

        private void PersistFreeGeometry()
        {
            _persistTimer.Stop();
            _settings.FreeLeft = Left;
            _settings.FreeTop = Top;
            _settings.FreeWidth = Width;
            _settings.FreeHeight = Height;
            _settings.Save();
        }

        /// <summary>Detach from the screen edge and float freely at the current spot.</summary>
        private void SwitchToFree()
        {
            if (_dock == DockEdge.Free) return;
            _dock = DockEdge.Free;
            _settings.DockEdge = DockEdge.Free;
            _settings.Save();
            ApplyLayout();
            SetExpanded(true);
            RestartCollapse(TimeSpan.FromSeconds(2.5));
        }

        /// <summary>Persist the user's custom position along the dock edge.</summary>
        private void SaveDockOffset()
        {
            _settings.DockOffset = _dock == DockEdge.Top ? _dockedHorizontalOffset : _dockedVerticalOffset;
            _settings.Save();
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
            => Dispatcher.InvokeAsync(() => { ApplyLayout(); SetExpanded(true); });

        /// <summary>Push the current settings onto the running window.</summary>
        private void ApplySettings()
        {
            Topmost = _settings.AlwaysOnTop;
            _dock = _settings.DockEdge;
            _pinned = _settings.AlwaysOpen;
            UpdatePinButton();
            ApplyLayout();

            // Open fully on launch; then slide away after a few seconds
            // (unless the shelf is pinned open).
            SetExpanded(true, animate: false);
            if (!_pinned) RestartCollapse(TimeSpan.FromSeconds(3));
        }

        /// <summary>Accent the pin button while the shelf is pinned open.</summary>
        private void UpdatePinButton()
        {
            PinButton.Foreground = _pinned
                ? new SolidColorBrush(Color.FromRgb(76, 194, 255))
                : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
            PinButton.ToolTip = _pinned ? "Always open - click to unpin" : "Always open";
        }

        // ==================================================================
        // Expand / collapse (slide-out animation)
        // ==================================================================

        private void SetExpanded(bool expand, bool animate = true, bool force = false)
        {
            // Animations can be disabled from the settings window.
            if (!_settings.Animate) animate = false;

            // Pinned shelves never collapse automatically, but a manual
            // (forced) collapse is still allowed and leaves the pin intact.
            if (!expand && _pinned && !force)
            {
                _collapseTimer.Stop();
                return;
            }

            if (_isExpanded == expand)
            {
                if (!expand) _collapseTimer.Stop();
                return;
            }

            _isExpanded = expand;
            _collapseTimer.Stop();
            _overLostAt = null; // restart hover debounce so the morph can settle

            if (_dock == DockEdge.Free)
            {
                // A manually-placed capsule (Expand on hover off) opens the shelf
                // centered on the capsule's spot, so it never jumps away.
                if (expand && !_settings.AutoOpenCapsule && Capsule.Visibility == Visibility.Visible)
                {
                    _freeL = Left + (CapsuleW - _freeW) / 2;
                    _freeT = Top;
                }

                double targetW = expand ? _freeW : CapsuleW;
                double targetH = expand ? _freeH : CapsuleH;
                double targetL = expand ? _freeL : _collapsedPosL;
                double targetT = expand ? _freeT : _collapsedPosT;

                // Hide the resize grips while collapsed so they don't steal clicks
                // from the capsule pill.
                ResizeOverlay.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;

                if (!animate || !IsLoaded)
                {
                    ResetShelfScale();
                    Width = targetW; Height = targetH; Left = targetL; Top = targetT;
                    ShelfBorder.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                    Capsule.Visibility = expand ? Visibility.Collapsed : Visibility.Visible;
                    return;
                }

                // Morph: the shelf condenses (scale) into the pill while the window
                // shrinks to its spot, and grows back out when expanding. The scale
                // is reset on completion/layout so it can't get stuck invisible.
                ShelfBorder.Visibility = Visibility.Visible;
                Capsule.Visibility = Visibility.Collapsed;
                Action? onCompleted = null;
                if (!expand)
                {
                    onCompleted = () =>
                    {
                        ShelfBorder.Visibility = Visibility.Collapsed;
                        Capsule.Visibility = Visibility.Visible;
                        ResetShelfScale();
                    };
                }
                else
                {
                    var sc = EnsureShelfScale();
                    sc.ScaleX = 0; sc.ScaleY = 0;
                    ShelfBorder.Opacity = 1;
                }

                // Capsule morph is deliberately slower than a docked slide (2x).
                AnimateWindow(toL: targetL, toT: targetT, toW: targetW, toH: targetH,
                              durationMs: _settings.AnimationMs * 2, onCompleted: onCompleted,
                              scaleTo: expand ? 1 : 0);
                return;
            }

            double target = expand ? _expandedPosL : _collapsedPosL;
            if (_dock == DockEdge.Top) target = expand ? _expandedPosT : _collapsedPosT;

            if (!animate || !IsLoaded)
            {
                SetPosition(target);
                return;
            }

            // Slide one axis (Left for side docks, Top for the top dock). Real
            // property values are set every tick so nothing is "held" afterwards.
            if (_dock == DockEdge.Top) AnimateWindow(toT: target, durationMs: _settings.AnimationMs);
            else AnimateWindow(toL: target, durationMs: _settings.AnimationMs);
        }

        /// <summary>
        /// Animates the window geometry by setting real property values on a short
        /// timer. The math is purely monotonic, so the window can never oscillate,
        /// and no held values block later manual moves/resizes.
        /// </summary>
        private void AnimateWindow(double? toL = null, double? toT = null,
                                   double? toW = null, double? toH = null,
                                   double durationMs = 170, Action? onCompleted = null,
                                   double? scaleTo = null)
        {
            StopAnimation();

            double fromL = Left, fromT = Top, fromW = Width, fromH = Height;
            double tl = toL ?? Left, tt = toT ?? Top, tw = toW ?? Width, th = toH ?? Height;

            // Scale the shelf content so it condenses into the pill smoothly.
            ScaleTransform? st = null;
            double fromScale = 1, toScale = 1;
            if (scaleTo.HasValue)
            {
                st = EnsureShelfScale();
                fromScale = st.ScaleX;
                toScale = scaleTo.Value;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
            _animTimer = timer;
            _animating = true;

            timer.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / durationMs);
                double eased = EaseInOut(t);
                if (toL.HasValue) Left = fromL + (tl - fromL) * eased;
                if (toT.HasValue) Top = fromT + (tt - fromT) * eased;
                if (toW.HasValue) Width = fromW + (tw - fromW) * eased;
                if (toH.HasValue) Height = fromH + (th - fromH) * eased;
                if (st != null) { double sc = fromScale + (toScale - fromScale) * eased; st.ScaleX = sc; st.ScaleY = sc; }

                if (t >= 1.0)
                {
                    timer.Stop();
                    _animTimer = null;
                    _animating = false;

                    if (toL.HasValue) Left = tl;
                    if (toT.HasValue) Top = tt;
                    if (toW.HasValue) Width = tw;
                    if (toH.HasValue) Height = th;
                    if (st != null) { st.ScaleX = 1; st.ScaleY = 1; } // reset

                    onCompleted?.Invoke();
                }
            };
            timer.Start();
        }

        private ScaleTransform EnsureShelfScale()
        {
            if (_shelfScale == null)
            {
                _shelfScale = new ScaleTransform(1, 1);
                ShelfBorder.RenderTransform = _shelfScale;
                ShelfBorder.RenderTransformOrigin = new Point(0.5, 0);
            }
            return _shelfScale;
        }

        private void StopAnimation()
        {
            if (_animTimer != null) { _animTimer.Stop(); _animTimer = null; }
            _animating = false;
        }

        private void ResetShelfScale()
        {
            if (_shelfScale != null) { _shelfScale.ScaleX = 1; _shelfScale.ScaleY = 1; }
            ShelfBorder.Opacity = 1;
        }

        private static double EaseInOut(double t)
            => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

        private void SetPosition(double pos)
        {
            if (_dock == DockEdge.Top) Top = pos;
            else Left = pos;
        }

        private void RestartCollapse(TimeSpan delay)
        {
            _collapseTimer.Stop();
            if (_pinned) return; // pinned shelves never collapse
            _collapseTimer.Interval = delay;
            _collapseTimer.Start();
        }

        private void CollapseTimer_Tick(object? sender, EventArgs e)
        {
            _collapseTimer.Stop();
            if (_isDraggingOut) return;
            if (_headerDragging) return; // don't collapse while dragging/holding the shelf
            SetExpanded(false);
        }

        // Hovering the window opens it; leaving (or the idle timer) slides it away.
        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            _collapseTimer.Stop();
            // A manually-placed capsule (Expand on hover off) only reacts to clicks,
            // never to hover, so it stays put while the cursor passes over it.
            if (_dock == DockEdge.Free && !_settings.AutoOpenCapsule && Capsule.Visibility == Visibility.Visible)
                return;
            if (!_animating) SetExpanded(true);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            // Hover expand/collapse is driven by HoverTimer_Tick using the real
            // OS cursor/window rect, so nothing to do here.
        }

        private void HoverTimer_Tick(object? sender, EventArgs e)
        {
            if (_isDraggingOut || _headerDragging || _capsuleDragging) return;

            bool over = IsMouseOverWindow();
            if (_isExpanded)
            {
                if (over || _animating || _pinned)
                {
                    _overLostAt = null;
                    _collapseTimer.Stop();
                }
                else
                {
                    // Debounce: the cursor must have been gone for ~250ms before we
                    // even schedule a collapse, so edge flicker can't vibrate it.
                    _overLostAt ??= DateTime.Now;
                    if ((DateTime.Now - _overLostAt.Value).TotalMilliseconds >= 250 &&
                        !_collapseTimer.IsEnabled)
                        RestartCollapse(TimeSpan.FromMilliseconds(_settings.CollapseDelayMs));
                }
            }
            else if (over && !_animating && (_settings.AutoOpenCapsule || _dock != DockEdge.Free))
            {
                // Collapsed + cursor over it. Free capsules respect the
                // "Expand on hover" setting; docked shelves always expand.
                _overLostAt ??= DateTime.Now;
                if ((DateTime.Now - _overLostAt.Value).TotalMilliseconds >= 150)
                    SetExpanded(true);
            }
        }

        /// <summary>
        /// Is the cursor inside the window? Uses OS-level GetCursorPos/GetWindowRect
        /// (both physical pixels) because WPF's Mouse.GetPosition mis-reports the
        /// position over a transparent window.
        /// </summary>
        private bool IsMouseOverWindow()
        {
            if (!GetCursorPos(out var pt)) return false;
            if (!GetWindowRect(new WindowInteropHelper(this).Handle, out var rc)) return false;
            return pt.X >= rc.Left && pt.X <= rc.Right &&
                   pt.Y >= rc.Top && pt.Y <= rc.Bottom;
        }

        // ==================================================================
        // Drag & drop INTO the shelf
        // ==================================================================

        /// <summary>Files/link/text can always be copied onto the shelf.</summary>
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = GetDropEffects(e.Data);
            e.Handled = true;
            SetExpanded(true); // pull the shelf out while a drag hovers it
        }

        /// <summary>Keep the shelf open and mirror the effect during the drag-over.</summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = GetDropEffects(e.Data);
            e.Handled = true;
            SetExpanded(true);
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
            => RestartCollapse(TimeSpan.FromMilliseconds(_settings.CollapseDelayMs));

        /// <summary>Accept file/folder drops, browser links and plain text snippets.</summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            bool added = AddFromData(e.Data);
            if (added)
            {
                SavePersisted();
                UpdateBadgeAndEmptyState();
                RestartCollapse(TimeSpan.FromSeconds(2.5)); // linger a moment after a drop
            }
        }

        private static DragDropEffects GetDropEffects(IDataObject data)
        {
            if (data.GetDataPresent(DataFormats.FileDrop) ||
                data.GetDataPresent(DataFormats.UnicodeText) ||
                data.GetDataPresent("UniformResourceLocator"))
                return DragDropEffects.Copy;
            return DragDropEffects.None;
        }

        private bool AddFromData(IDataObject data)
        {
            bool any = false;

            if (data.GetDataPresent(DataFormats.FileDrop) &&
                data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                foreach (string p in paths)
                    if (AddPath(p)) any = true;
            }
            else if (data.GetDataPresent("UniformResourceLocator") &&
                     data.GetData("UniformResourceLocator") is string url && !string.IsNullOrWhiteSpace(url))
            {
                if (AddUrl(url.Trim())) any = true;
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText) &&
                     data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
            {
                text = text.Trim();
                if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "mailto" || uri.Scheme == "ftp"))
                {
                    if (AddUrl(text)) any = true;
                }
                else if (AddSnippet(text)) any = true;
            }

            return any;
        }

        private bool AddPath(string path)
        {
            bool isDir = Directory.Exists(path);
            bool isFile = File.Exists(path);
            if (!isDir && !isFile) return false;

            string name = isDir ? new DirectoryInfo(path).Name : Path.GetFileName(path);

            var item = new ShelfItem
            {
                Kind = isDir ? ShelfItemKind.Folder : ShelfItemKind.File,
                Name = string.IsNullOrEmpty(name) ? path : name,
                Path = path,
                Extension = isDir ? null : Path.GetExtension(path).TrimStart('.').ToUpperInvariant()
            };

            if (_items.Any(i => i.Kind == item.Kind && i.Path == item.Path)) return false; // no dupes

            item.Icon = LoadIcon(item);
            _items.Add(item);
            return true;
        }

        private bool AddUrl(string url)
        {
            if (_items.Any(i => i.Url == url)) return false;
            string name = Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Host.Length > 0
                ? u.Host
                : "Link";
            var item = new ShelfItem { Kind = ShelfItemKind.Url, Name = name, Url = url };
            item.Icon = LoadIcon(item);
            _items.Add(item);
            return true;
        }

        private bool AddSnippet(string text)
        {
            if (_items.Any(i => i.Text == text)) return false;
            string firstLine = text.Split('\n')[0].Trim();
            var item = new ShelfItem
            {
                Kind = ShelfItemKind.Snippet,
                Name = string.IsNullOrEmpty(firstLine) ? "Snippet" : TruncateSnippet(firstLine, 40),
                Text = text
            };
            item.Icon = LoadIcon(item);
            _items.Add(item);
            return true;
        }

        private static string TruncateSnippet(string s, int max)
            => s.Length <= max ? s : s[..max] + "…";

        // ==================================================================
        // Item preview icons
        // ==================================================================

        private static ImageSource? LoadIcon(ShelfItem item)
        {
            switch (item.Kind)
            {
                case ShelfItemKind.File:
                    if (item.Path != null && ImageExtensions.Contains(Path.GetExtension(item.Path).ToLowerInvariant()))
                    {
                        var thumb = LoadImageThumbnail(item.Path);   // real pixel preview for images
                        if (thumb != null) return thumb;
                    }
                    return item.Path != null ? LoadShellIcon(item.Path, false) : null;

                case ShelfItemKind.Folder:
                    return item.Path != null ? LoadShellIcon(item.Path, true) : null;

                case ShelfItemKind.Url:
                    return MakeGlyph("\uE71B", 18, Colors.White, 22); // "Link" glyph

                case ShelfItemKind.Snippet:
                    return MakeGlyph("{}", 13, Colors.White, 22);     // code-ish glyph

                default:
                    return null;
            }
        }

        private static ImageSource? LoadImageThumbnail(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                // Cap only the width so the height scales proportionally;
                // setting both would force the image into a square box.
                bmp.DecodePixelWidth = 64;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        private static ImageSource? LoadShellIcon(string path, bool isDirectory)
        {
            var info = new SHFILEINFO();
            uint attrs = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            IntPtr res = SHGetFileInfo(path, attrs, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);
            if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

            try
            {
                return Imaging.CreateBitmapSourceFromHIcon(info.hIcon,
                    Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        /// <summary>Render a tiny glyph (Segoe MDL2 Assets or text) into an ImageSource.</summary>
        private static ImageSource MakeGlyph(string text, double fontSize, Color foreground, double box)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var dpi = Application.Current.MainWindow is { } w
                    ? VisualTreeHelper.GetDpi(w)
                    : new DpiScale(1, 1);

                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                    null, new Rect(0, 0, box, box), 6, 6);

                var typeface = text == "{}"
                    ? new Typeface("Segoe UI")
                    : new Typeface("Segoe MDL2 Assets");

                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, typeface, fontSize,
                    new SolidColorBrush(foreground), dpi.PixelsPerDip);

                dc.DrawText(ft, new Point((box - ft.Width) / 2, (box - ft.Height) / 2));
            }

            var rtb = new RenderTargetBitmap((int)box, (int)box, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        // ==================================================================
        // Drag & drop OUT of the shelf
        // ==================================================================

        /// <summary>Remember where the press started and which card was hit.</summary>
        private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source)
            {
                _dragCandidate = FindItem(source);
                _dragStart = e.GetPosition(this);
            }
        }

        private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
            => _dragCandidate = null;

        /// <summary>
        /// Once the mouse has moved past the drag threshold with the button held,
        /// hand the card payload to the OLE engine. Items stay pinned afterwards.
        /// </summary>
        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate is null || e.LeftButton != MouseButtonState.Pressed) return;

            Point pos = e.GetPosition(this);
            bool moved = Math.Abs(pos.X - _dragStart.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                         Math.Abs(pos.Y - _dragStart.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!moved) return;

            ShelfItem item = _dragCandidate;
            _dragCandidate = null;
            _isDraggingOut = true;
            _collapseTimer.Stop();

            try
            {
                // Standard OLE drag-out — zero conversion, the raw payload goes straight to the target.
                DragDrop.DoDragDrop(this, BuildDragData(item), DragDropEffects.Copy);
            }
            finally
            {
                _isDraggingOut = false;
                RestartCollapse(TimeSpan.FromMilliseconds(1200));
            }
        }

        private static IDataObject BuildDragData(ShelfItem item)
        {
            var data = new DataObject();
            switch (item.Kind)
            {
                case ShelfItemKind.File:
                case ShelfItemKind.Folder:
                    if (!string.IsNullOrEmpty(item.Path))
                        data.SetData(DataFormats.FileDrop, new[] { item.Path });
                    break;
                case ShelfItemKind.Url:
                    if (!string.IsNullOrEmpty(item.Url))
                    {
                        data.SetText(item.Url);
                        data.SetData("UniformResourceLocator", item.Url);
                    }
                    break;
                case ShelfItemKind.Snippet:
                    data.SetText(item.Text ?? "");
                    break;
            }
            return data;
        }

        private static ShelfItem? FindItem(DependencyObject source)
        {
            DependencyObject? cur = source;
            while (cur is not null)
            {
                if (cur is FrameworkElement fe && fe.DataContext is ShelfItem item) return item;
                cur = VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        // ==================================================================
        // Removal / clearing / badge
        // ==================================================================

        private void RemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ShelfItem item)
            {
                _items.Remove(item);
                SavePersisted();
                UpdateBadgeAndEmptyState();
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            SavePersisted();
            UpdateBadgeAndEmptyState();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => UpdateBadgeAndEmptyState();

        private void UpdateBadgeAndEmptyState()
        {
            EmptyState.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ClearAllButton.IsEnabled = _items.Count > 0;
        }

        // ==================================================================
        // Dock switching / context menu
        // ==================================================================

        private void DockRight_Click(object sender, RoutedEventArgs e) => SetDock(DockEdge.Right);
        private void DockLeft_Click(object sender, RoutedEventArgs e)  => SetDock(DockEdge.Left);
        private void DockTop_Click(object sender, RoutedEventArgs e)   => SetDock(DockEdge.Top);
        private void DockFree_Click(object sender, RoutedEventArgs e)  => SwitchToFree();

        private void SetDock(DockEdge edge)
        {
            if (edge == DockEdge.Free)
            {
                SwitchToFree();
                return;
            }
            if (_dock == edge) return;
            _dock = edge;
            _settings.DockEdge = edge;
            _settings.Save();
            ApplyLayout();
            SetExpanded(true);
            RestartCollapse(TimeSpan.FromSeconds(2.5));
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        // ==================================================================
        // System tray icon
        // ==================================================================

        private void CreateTrayIcon()
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = TrayIconFactory.Create(),
                Text = "Smart Drop Zone",
                Visible = true
            };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Show / Hide shelf", null, (_, _) => ToggleShelf());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Dock left", null, (_, _) => SetDock(DockEdge.Left));
            menu.Items.Add("Dock top", null, (_, _) => SetDock(DockEdge.Top));
            menu.Items.Add("Dock right", null, (_, _) => SetDock(DockEdge.Right));
            menu.Items.Add("Free position", null, (_, _) => SwitchToFree());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());
            _notifyIcon.ContextMenuStrip = menu;

            // Left-click toggles the shelf; right-click opens the menu.
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == WinForms.MouseButtons.Left) ToggleShelf();
            };
        }

        private void ToggleShelf()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
                SetExpanded(true);
                RestartCollapse(TimeSpan.FromSeconds(3));
            }
        }

        private void OpenSettings()
        {
            // Non-modal + live: the shelf stays fully usable while settings are open.
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.SettingsChanged += OnSettingsApplied;
            _settingsWindow.Show();
        }

        private void OnSettingsApplied(AppSettings updated)
        {
            // Merge onto the existing instance rather than replacing it, so live
            // state (free geometry, capsule spot) is never clobbered by the
            // settings window's snapshot.
            _settings.DockEdge = updated.DockEdge;
            _settings.AlwaysOnTop = updated.AlwaysOnTop;
            _settings.AlwaysOpen = updated.AlwaysOpen;
            _settings.CollapseDelayMs = updated.CollapseDelayMs;
            _settings.AnimationMs = updated.AnimationMs;
            _settings.Animate = updated.Animate;
            _settings.StartWithWindows = updated.StartWithWindows;
            _settings.AutoOpenCapsule = updated.AutoOpenCapsule;
            _settings.HoldToDetach = updated.HoldToDetach;
            _settings.HoldToDock = updated.HoldToDock;
            _settings.HoldDelayMs = updated.HoldDelayMs;
            _settings.HoldFillMs = updated.HoldFillMs;
            _settings.SortMode = updated.SortMode;
            _settings.ViewMode = updated.ViewMode;
            _settings.Save();
            _settings.ApplyStartWithWindows();
            Topmost = _settings.AlwaysOnTop;
            _pinned = _settings.AlwaysOpen;
            UpdatePinButton();
            SetDock(_settings.DockEdge);
            ApplySort(_settings.SortMode);
            ApplyView(_settings.ViewMode);

            if (_pinned) { _collapseTimer.Stop(); SetExpanded(true); }
            else RestartCollapse(TimeSpan.FromSeconds(2.5));
        }

        // ==================================================================
        // Sorting / view (Explorer-style)
        // ==================================================================

        private void SortName_Click(object sender, RoutedEventArgs e) => SetSort(SortMode.Name);
        private void SortType_Click(object sender, RoutedEventArgs e) => SetSort(SortMode.Type);
        private void SortDate_Click(object sender, RoutedEventArgs e) => SetSort(SortMode.DateAdded);

        private void ViewList_Click(object sender, RoutedEventArgs e) => SetView(ViewMode.List);
        private void ViewIcons_Click(object sender, RoutedEventArgs e) => SetView(ViewMode.Icons);

        private void SetSort(SortMode mode)
        {
            if (_settings.SortMode == mode) return;
            _settings.SortMode = mode;
            _settings.Save();
            ApplySort(mode);
            UpdateViewMenuChecks();
        }

        private void SetView(ViewMode mode)
        {
            if (_settings.ViewMode == mode) return;
            _settings.ViewMode = mode;
            _settings.Save();
            ApplyView(mode);
            UpdateViewMenuChecks();
        }

        private void ApplySort(SortMode mode)
        {
            _sortMode = mode;
            _view.CustomSort = new ShelfItemComparer(mode);
            _view.Refresh();
        }

        private void ApplyView(ViewMode mode)
        {
            ItemList.ItemsPanel = (ItemsPanelTemplate)FindResource(mode == ViewMode.Icons ? "GridPanel" : "ListPanel");
            ItemList.ItemTemplate = (DataTemplate)FindResource(mode == ViewMode.Icons ? "GridCardTemplate" : "ListCardTemplate");
            ItemList.ItemContainerStyle = (Style)FindResource(mode == ViewMode.Icons ? "GridContainerStyle" : "CardItemStyle");
        }

        private void UpdateViewMenuChecks()
        {
            SortNameItem.IsChecked = _settings.SortMode == SortMode.Name;
            SortTypeItem.IsChecked = _settings.SortMode == SortMode.Type;
            SortDateItem.IsChecked = _settings.SortMode == SortMode.DateAdded;
            ViewListItem.IsChecked = _settings.ViewMode == ViewMode.List;
            ViewIconsItem.IsChecked = _settings.ViewMode == ViewMode.Icons;
        }

        /// <summary>Explorer-style "Sort" dropdown in the toolbar.</summary>
        private void SortButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            AddCheckItem(menu, "Name", _settings.SortMode == SortMode.Name, () => SetSort(SortMode.Name));
            AddCheckItem(menu, "Type", _settings.SortMode == SortMode.Type, () => SetSort(SortMode.Type));
            AddCheckItem(menu, "Date added", _settings.SortMode == SortMode.DateAdded, () => SetSort(SortMode.DateAdded));
            ShowMenu(menu, (FrameworkElement)sender);
        }

        /// <summary>Explorer-style "View" dropdown in the toolbar.</summary>
        private void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            AddCheckItem(menu, "List", _settings.ViewMode == ViewMode.List, () => SetView(ViewMode.List));
            AddCheckItem(menu, "Icons", _settings.ViewMode == ViewMode.Icons, () => SetView(ViewMode.Icons));
            ShowMenu(menu, (FrameworkElement)sender);
        }

        private static void AddCheckItem(ContextMenu menu, string header, bool isChecked, Action onClick)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = isChecked
            };
            item.Click += (_, _) => onClick();
            menu.Items.Add(item);
        }

        private static void ShowMenu(ContextMenu menu, FrameworkElement target)
        {
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>Orders shelf cards: folders first, then by the selected key.</summary>
        private sealed class ShelfItemComparer : IComparer
        {
            private readonly SortMode _mode;

            public ShelfItemComparer(SortMode mode) => _mode = mode;

            public int Compare(object? x, object? y)
            {
                if (x is not ShelfItem a || y is not ShelfItem b) return 0;

                int folder = (b.IsFolder ? 1 : 0) - (a.IsFolder ? 1 : 0);
                if (folder != 0) return folder;

                return _mode switch
                {
                    SortMode.Type => CompareType(a, b),
                    SortMode.DateAdded => b.AddedAt.CompareTo(a.AddedAt),
                    _ => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase)
                };
            }

            private static int CompareType(ShelfItem a, ShelfItem b)
            {
                int kind = a.Kind.CompareTo(b.Kind);
                if (kind != 0) return kind;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            }
        }

        // ==================================================================
        // Header buttons: collapse / pin
        // ==================================================================

        /// <summary>
        /// Start dragging the shelf by the header. Docked shelves can be moved
        /// along the edge; holding one out of its dock fills the ring and detaches
        /// it into free mode.
        /// </summary>
        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d &&
                (d as FrameworkElement)?.TemplatedParent is Button) return;

            _headerDragging = true;
            _holdStartedAt = null;
            _lastHeaderScreen = PointToScreen(e.GetPosition(this));
            CaptureMouse();
            e.Handled = true;
        }

        /// <summary>Keep the shelf under the cursor while the header is held.</summary>
        private void Window_HeaderDrag_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_headerDragging) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndHeaderDrag();
                return;
            }

            Point screen = PointToScreen(e.GetPosition(this));
            Left += screen.X - _lastHeaderScreen.X;
            Top += screen.Y - _lastHeaderScreen.Y;
            _lastHeaderScreen = screen;
            UpdateHold();
            e.Handled = true;
        }

        private void Window_HeaderDrag_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_headerDragging) return;
            EndHeaderDrag();
            e.Handled = true;
        }

        private void EndHeaderDrag()
        {
            _headerDragging = false;
            ReleaseMouseCapture();
            StopHold();

            if (_dock == DockEdge.Free)
            {
                _persistTimer.Stop();
                PersistFreeGeometry();
            }
            else
            {
                if (_dock == DockEdge.Top) _dockedHorizontalOffset = Left;
                else _dockedVerticalOffset = Top;
                Reanchor();
                SaveDockOffset();
            }
        }

        /// <summary>
        /// Picks the active hold gesture:
        ///  - free shelf near a screen edge  -> dock to it
        ///  - docked shelf pulled out of its edge into open space -> detach to free
        ///  - docked shelf held against a DIFFERENT edge -> switch docks directly
        /// </summary>
        private void UpdateHold()
        {
            bool active;
            _holdTargetEdge = null;
            if (_dock == DockEdge.Free)
            {
                active = _settings.HoldToDock && IsNearDockEdge();
                _holdIsDock = active;
            }
            else if (_settings.HoldToDock && IsNearDifferentDockEdge(out var edge))
            {
                _holdIsDock = true;
                _holdTargetEdge = edge;
                active = true;
            }
            else
            {
                active = _settings.HoldToDetach && IsOutOfDock();
                _holdIsDock = false;
            }
            if (!active)
            {
                StopHold();
                return;
            }

            // Show where the shelf will dock; hide the preview when detaching to free.
            if (_holdIsDock)
                ShowSnapPreview(_holdTargetEdge ?? NearestEdge());
            else
                HideSnapPreview();

            // While the user is waiting out the hold, never let the shelf collapse.
            _collapseTimer.Stop();

            Point cur = _lastHeaderScreen;
            if (_holdStartedAt is null)
            {
                _holdStartedAt = DateTime.Now;
                _holdBaseScreen = cur;
                SetHoldRingColor(_holdIsDock);
                PositionHoldRing();
                // Ring stays hidden during the idle pause; the timer shows it at fill start.
                HoldRing.Visibility = Visibility.Collapsed;
                HoldRingArc.Data = ArcGeometry(0);
                _holdTimer?.Start();
            }
            else if (Distance(cur, _holdBaseScreen) > HoldMoveReset)
            {
                // Moved while holding - restart the fill from scratch.
                _holdStartedAt = DateTime.Now;
                _holdBaseScreen = cur;
                PositionHoldRing();
                HoldRingArc.Data = ArcGeometry(0);
            }
        }

        /// <summary>
        /// Centers the hold ring on the cursor and clamps it to the work area so
        /// it always stays fully visible, even when the mouse is at a screen edge.
        /// </summary>
        private void PositionHoldRing()
        {
            Rect wa = SystemParameters.WorkArea;
            double rx = Math.Clamp(_lastHeaderScreen.X - HoldRing.Width / 2, wa.Left, wa.Right - HoldRing.Width);
            double ry = Math.Clamp(_lastHeaderScreen.Y - HoldRing.Height / 2, wa.Top, wa.Bottom - HoldRing.Height);
            Point tl = PointToScreen(new Point(0, 0));
            HoldRing.Margin = new Thickness(rx - tl.X, ry - tl.Y, 0, 0);
        }

        /// <summary>
        /// A translucent overlay showing where the shelf will dock when the hold
        /// completes.  Mimics the Aero-snap preview.
        /// </summary>
        private void ShowSnapPreview(DockEdge edge)
        {
            if (edge == _dock) { HideSnapPreview(); return; }
            var w = EnsureSnapPreview();
            Rect r = DockRectFor(edge);
            w.Left = r.Left; w.Top = r.Top; w.Width = r.Width; w.Height = r.Height;
            w.Show();
        }

        private void HideSnapPreview()
        {
            if (_snapPreview != null && _snapPreview.IsVisible) _snapPreview.Hide();
        }

        private Window EnsureSnapPreview()
        {
            if (_snapPreview == null)
            {
                _snapPreview = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Width = 0, Height = 0,
                    Content = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(0x33, 0x4C, 0xC2, 0xFF)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(0xCC, 0x4C, 0xC2, 0xFF)),
                        BorderThickness = new Thickness(2),
                        CornerRadius = new CornerRadius(10)
                    }
                };
                _snapPreview.SourceInitialized += (_, _) =>
                {
                    IntPtr hwnd = new WindowInteropHelper(_snapPreview).Handle;
                    int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
                    SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
                };
                _snapPreview.Show();
            }
            return _snapPreview;
        }

        /// <summary>Where the shelf will land when it docks to the given edge.</summary>
        private Rect DockRectFor(DockEdge edge)
        {
            Rect wa = SystemParameters.WorkArea;
            double w = Width, h = Height;
            switch (edge)
            {
                case DockEdge.Top:
                    return new Rect(Math.Clamp(Left, wa.Left, wa.Right - w), wa.Top, w, h);
                case DockEdge.Right:
                    return new Rect(wa.Right - w, Math.Clamp(Top, wa.Top, wa.Bottom - h), w, h);
                case DockEdge.Left:
                    return new Rect(wa.Left, Math.Clamp(Top, wa.Top, wa.Bottom - h), w, h);
                default:
                    return new Rect(Left, Top, w, h);
            }
        }

        /// <summary>The screen edge closest to the shelf's current position.</summary>
        private DockEdge NearestEdge()
        {
            Rect wa = SystemParameters.WorkArea;
            double dL = Left - wa.Left, dR = wa.Right - (Left + Width), dT = Top - wa.Top;
            if (dL <= dR && dL <= dT) return DockEdge.Left;
            if (dR <= dT) return DockEdge.Right;
            return DockEdge.Top;
        }

        private bool IsOutOfDock()
        {
            Rect wa = SystemParameters.WorkArea;
            return _dock switch
            {
                DockEdge.Right => Math.Abs(Left - (wa.Right - Width)) > OutOfDockThreshold,
                DockEdge.Left => Math.Abs(Left - wa.Left) > OutOfDockThreshold,
                DockEdge.Top => Math.Abs(Top - wa.Top) > OutOfDockThreshold,
                _ => false
            };
        }

        /// <summary>Is the free shelf close enough to a dockable edge (left/right/top)?</summary>
        private bool IsNearDockEdge()
        {
            Rect wa = SystemParameters.WorkArea;
            double dLeft = Left - wa.Left;
            double dRight = wa.Right - (Left + Width);
            double dTop = Top - wa.Top;
            return dLeft <= SnapThreshold || dRight <= SnapThreshold || dTop <= SnapThreshold;
        }

        /// <summary>Like <see cref="IsNearDockEdge"/> but excludes the current dock edge.</summary>
        private bool IsNearDifferentDockEdge(out DockEdge edge)
        {
            Rect wa = SystemParameters.WorkArea;
            double dLeft = Left - wa.Left;
            double dRight = wa.Right - (Left + Width);
            double dTop = Top - wa.Top;

            // Pick the nearest edge, ignoring the one the shelf is currently on.
            // (The current edge's distance is irrelevant — the user is dragging AWAY
            // from it, so e.g. a right-docked shelf can be near both the right edge
            // and the top edge at once.)
            DockEdge cur = _dock;
            DockEdge? best = null;
            double bestD = double.MaxValue;
            if (cur != DockEdge.Left && dLeft < bestD) { best = DockEdge.Left; bestD = dLeft; }
            if (cur != DockEdge.Right && dRight < bestD) { best = DockEdge.Right; bestD = dRight; }
            if (cur != DockEdge.Top && dTop < bestD) { best = DockEdge.Top; bestD = dTop; }

            if (best is DockEdge b && bestD <= SnapThreshold) { edge = b; return true; }
            edge = default; return false;
        }

        /// <summary>Switch to a different dock directly, keeping the shelf's current position along the new edge.</summary>
        private void SwitchDockDirect(DockEdge edge)
        {
            if (_dock == edge) return;
            _dock = edge;
            _settings.DockEdge = edge;
            if (edge == DockEdge.Top) _dockedHorizontalOffset = Left;
            else _dockedVerticalOffset = Top;
            _settings.Save();
            ApplyLayout();
            SetExpanded(true);
            RestartCollapse(TimeSpan.FromSeconds(2.5));
        }

        private void HoldTimer_Tick(object? sender, EventArgs e)
        {
            if (_holdStartedAt is null) return;

            double elapsed = (DateTime.Now - _holdStartedAt.Value).TotalMilliseconds;

            // Idle pause: nothing is shown yet (the empty gray ring stays hidden).
            // Only when the fill starts does the ring appear and sweep.
            if (elapsed < _settings.HoldDelayMs)
            {
                HoldRing.Visibility = Visibility.Collapsed;
                return;
            }

            HoldRing.Visibility = Visibility.Visible;
            double fill = (elapsed - _settings.HoldDelayMs) / _settings.HoldFillMs;
            if (fill >= 1.0)
            {
                StopHold();
                if (_holdIsDock)
                {
                    if (_holdTargetEdge is DockEdge targetEdge)
                        SwitchDockDirect(targetEdge);
                    else
                        SnapToNearestDock();
                }
                else DetachToFree();
                return;
            }
            HoldRingArc.Data = ArcGeometry(360 * fill);
        }

        private void StopHold()
        {
            _holdStartedAt = null;
            _holdTimer?.Stop();
            HoldRing.Visibility = Visibility.Collapsed;
            HideSnapPreview();
        }

        /// <summary>Switch to free mode while keeping the shelf exactly where it is.</summary>
        private void DetachToFree()
        {
            if (_dock == DockEdge.Free) return;
            _dock = DockEdge.Free;
            _settings.DockEdge = DockEdge.Free;
            _settings.FreeLeft = Left;
            _settings.FreeTop = Top;
            _settings.FreeWidth = Width;
            _settings.FreeHeight = Height;
            _settings.Save();
            ApplyLayout();
            SetExpanded(true);
        }

        /// <summary>Snap the free shelf to the closest screen edge it is being held against.</summary>
        private void SnapToNearestDock()
        {
            Rect wa = SystemParameters.WorkArea;
            double dLeft = Left - wa.Left;
            double dRight = wa.Right - (Left + Width);
            double dTop = Top - wa.Top;
            if (dTop <= SnapThreshold && dTop <= dLeft && dTop <= dRight)
                _dock = DockEdge.Top;
            else if (dRight <= SnapThreshold && dRight <= dLeft)
                _dock = DockEdge.Right;
            else if (dLeft <= SnapThreshold)
                _dock = DockEdge.Left;
            else
                return;

            _settings.DockEdge = _dock;
            if (_dock == DockEdge.Top) _dockedHorizontalOffset = Left;
            else _dockedVerticalOffset = Top;
            _settings.Save();
            ApplyLayout();
            SetExpanded(true);
            RestartCollapse(TimeSpan.FromSeconds(2.5));
        }

        private void SetHoldRingColor(bool isDock)
        {
            HoldRingArc.Stroke = isDock
                ? new SolidColorBrush(Color.FromRgb(108, 203, 95)) // green = will dock
                : (Brush)FindResource("AccentBrush");              // blue = will detach
        }

        /// <summary>An arc (clockwise from 12 o'clock) for the hold ring.</summary>
        private static Geometry ArcGeometry(double sweepDegrees)
        {
            const double cx = 23, cy = 23, r = 18;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                Point start = new Point(cx, cy - r);
                if (sweepDegrees <= 0) return g;
                double rad = Math.Min(sweepDegrees, 359.9) * Math.PI / 180;
                Point end = new Point(cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
            }
            return g;
        }

        private static double Distance(Point a, Point b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private void Capsule_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement el) return;
            el.CaptureMouse();
            _capsuleDragging = true;
            _capsuleMoved = false;
            _capsuleDragStart = e.GetPosition(this);
            e.Handled = true;
        }

        private void Capsule_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_capsuleDragging) return;

            // The button can be released (or capture lost) before MouseLeftButtonUp
            // arrives; DragMove() throws if the primary button isn't down.
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _capsuleDragging = false;
                _capsuleMoved = false;
                if (sender is UIElement el) el.ReleaseMouseCapture();
                return;
            }

            // Only start moving once the cursor actually travels a few pixels,
            // so a plain click can still open the shelf.
            if (!_capsuleMoved)
            {
                Point p = e.GetPosition(this);
                if (Math.Abs(p.X - _capsuleDragStart.X) <= 3 &&
                    Math.Abs(p.Y - _capsuleDragStart.Y) <= 3) return;
                _capsuleMoved = true;
            }
            DragMove();
        }

        private void Capsule_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _capsuleDragging = false;
            if (sender is UIElement el) el.ReleaseMouseCapture();

            if (_capsuleMoved)
            {
                // Manual capsule: remember where it was dragged.
                _capsuleL = Left;
                _capsuleT = Top;
                _settings.FreeCapsuleLeft = _capsuleL;
                _settings.FreeCapsuleTop = _capsuleT;
                _settings.Save();
            }
            else if (!_isExpanded)
            {
                // A plain click on the collapsed capsule opens the shelf.
                SetExpanded(true);
            }
        }

        /// <summary>Slide the shelf away to the edge handle. Keeps the pin setting as-is.</summary>
        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            SetExpanded(false, force: true);
        }

        /// <summary>Pin the shelf open so it never auto-collapses.</summary>
        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            _settings.AlwaysOpen = _pinned;
            _settings.Save();
            UpdatePinButton();

            if (_pinned)
            {
                _collapseTimer.Stop();
                SetExpanded(true);
            }
            else
            {
                RestartCollapse(TimeSpan.FromSeconds(2.5));
            }
        }

        // ==================================================================
        // Persistence (JSON in %AppData%\SmartDropZone\shelf.json)
        // ==================================================================

        private void LoadPersisted()
        {
            try
            {
                if (!File.Exists(PersistFile)) return;
                string json = File.ReadAllText(PersistFile);
                var list = JsonSerializer.Deserialize<List<ShelfItem>>(json, JsonOptions);
                if (list is null) return;

                foreach (ShelfItem item in list)
                {
                    if (string.IsNullOrEmpty(item.Name)) continue;
                    item.Icon = LoadIcon(item);
                    _items.Add(item);
                }
            }
            catch
            {
                // Corrupt or unreadable shelf file — start fresh.
                _items.Clear();
            }
        }

        private void SavePersisted()
        {
            try
            {
                string dir = Path.GetDirectoryName(PersistFile)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(PersistFile, JsonSerializer.Serialize(_items.ToList(), JsonOptions));
            }
            catch
            {
                // Persistence is best-effort; the shelf still works in memory.
            }
        }
    }
}