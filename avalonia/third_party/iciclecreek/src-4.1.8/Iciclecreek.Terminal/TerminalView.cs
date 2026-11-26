using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Fonts.Tables.Cmap;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Rendering;
using Avalonia.Threading;
using Iciclecreek.Avalonia.Terminal;
using Iciclecreek.Terminal.Skia;
using Porta.Pty;
using SkiaSharp;
using XTerm;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Events;
using XTerm.Graphics;
using XTerm.Input;
using XTerm.Options;
using XTerm.Search;
using XTerm.Selection;

namespace Iciclecreek.Terminal;

public class TerminalView : Control, ICustomHitTest, IDisposable
{
	internal sealed record CachedTextRun(FormattedText? Text, int StartX, int CellCount, IBrush? Background, LinePlacement? Placement = null, TerminalImage? Image = null, UnderlineStyle UnderlineStyle = UnderlineStyle.None, IBrush? UnderlineBrush = null, GlyphRun? Glyphs = null, IBrush? Foreground = null, int[]? Blocks = null, SixelStrip? Strip = null)
	{
		public bool IsImage
		{
			get
			{
				if (Placement.HasValue)
				{
					return Image != null;
				}
				return false;
			}
		}

		public bool IsStrip => Strip != null;

		public bool IsBlockArt => Blocks != null;

		public Geometry? UnderlineGeometry { get; set; }

		public IPen? UnderlinePen { get; set; }
	}

	private sealed class CachedBitmap
	{
		public Bitmap? Bitmap;

		public int FrameSerial;
	}

	private sealed class CachedStripBitmap
	{
		public Bitmap? Bitmap;

		public long Version;
	}

	public readonly record struct VisibleMark(int ViewportRow, int BufferRow, ShellIntegrationMark Kind, int? ExitCode);

	internal sealed class HoveredUrl
	{
		public string Url { get; }

		public bool FromSequence { get; }

		public List<(int Line, int StartCol, int EndCol)> Segments { get; }

		public HoveredUrl(string url, List<(int Line, int StartCol, int EndCol)> segments, bool fromSequence = false)
		{
			Url = url;
			Segments = segments;
			FromSequence = fromSequence;
		}

		public bool Contains(int line, int col)
		{
			foreach (var segment in Segments)
			{
				if (segment.Line == line && col >= segment.StartCol && col <= segment.EndCol)
				{
					return true;
				}
			}
			return false;
		}

		public bool SameAs(HoveredUrl? other)
		{
			if (other != null && Url == other.Url && other.Segments.Count == Segments.Count)
			{
				(int, int, int) tuple = other.Segments[0];
				(int, int, int) tuple2 = Segments[0];
				if (tuple.Item1 == tuple2.Item1 && tuple.Item2 == tuple2.Item2)
				{
					return tuple.Item3 == tuple2.Item3;
				}
				return false;
			}
			return false;
		}
	}

	private readonly record struct SizedBlockDraw(BufferLine Line, LineSizedRun Run, double StartYPos, double RowHeight);

	private readonly record struct FrontImageDraw(CachedTextRun Run, double StartYPos, double RowHeight);

	private sealed class TerminalInputMethodClient : TextInputMethodClient
	{
		private readonly TerminalView _view;

		private string? _preeditText;

		public string? PreeditText => _preeditText;

		public override Visual TextViewVisual => _view;

		public override bool SupportsPreedit => true;

		public override bool SupportsSurroundingText => true;

		public override string SurroundingText => LineAndCaret().Text;

		public override Rect CursorRectangle
		{
			get
			{
				try
				{
					TerminalBuffer buffer = _view._terminal.Buffer;
					int x = buffer.X;
					int num = buffer.YBase + buffer.Y;
					int viewportY = buffer.ViewportY;
					int num2 = num - viewportY;
					double x2 = (double)x * _view._charWidth + Math.Max(0.0, _view.GutterWidth);
					double y = (double)num2 * _view._charHeight;
					return new Rect(x2, y, _view._charWidth, _view._charHeight);
				}
				catch
				{
					return default;
				}
			}
		}

		public override TextSelection Selection
		{
			get
			{
				int item = LineAndCaret().Caret;
				return new TextSelection(item, item);
			}
			set
			{
			}
		}

		public TerminalInputMethodClient(TerminalView view)
		{
			_view = view;
		}

		private (string Text, int Caret) LineAndCaret()
		{
			try
			{
				TerminalBuffer buffer = _view._terminal.Buffer;
				int y = buffer.YBase + buffer.Y;
				BufferLine line = buffer.GetLine(y);
				if (line == null)
				{
					return (Text: string.Empty, Caret: 0);
				}
				int x = buffer.X;
				StringBuilder stringBuilder = new StringBuilder();
				int item = 0;
				for (int i = 0; i < line.Length; i++)
				{
					if (i == x)
					{
						item = stringBuilder.Length;
					}
					BufferCell bufferCell = line[i];
					if (bufferCell.Width == 0)
					{
						if (!string.IsNullOrEmpty(bufferCell.Content))
						{
							stringBuilder.Append(bufferCell.Content);
						}
					}
					else
					{
						stringBuilder.Append(string.IsNullOrEmpty(bufferCell.Content) ? " " : bufferCell.Content);
					}
				}
				if (x >= line.Length)
				{
					item = stringBuilder.Length;
				}
				return (Text: stringBuilder.ToString(), Caret: item);
			}
			catch
			{
				return (Text: string.Empty, Caret: 0);
			}
		}

		public override void SetPreeditText(string? preeditText)
		{
			_preeditText = preeditText;
			_view.RequestInvalidate();
		}

		public override void SetPreeditText(string? preeditText, int? cursorPos)
		{
			_preeditText = preeditText;
			_view.RequestInvalidate();
		}

		public void ClearPreeditText()
		{
			if (_preeditText != null)
			{
				_preeditText = null;
				_view.RequestInvalidate();
			}
		}

		internal void NotifyCursorRectangleChanged()
		{
			RaiseCursorRectangleChanged();
		}

		internal void NotifySurroundingTextChanged()
		{
			RaiseSurroundingTextChanged();
		}
	}

	internal sealed class OutputCounters
	{
		public long Bytes;

		public long Chunks;

		public long LockWaitTicks;

		public long WriteTicks;
	}

	private sealed class PictureGroup
	{
		public required TerminalImage Image;

		public Rect Mapping;

		public int ZIndex;

		public int Serial;

		public readonly List<Rect> Clips = new List<Rect>();
	}

	private XTerm.Terminal _terminal;

	private readonly SkiaPictureCache _pictures = new SkiaPictureCache();

	private long _pictureFrame;

	private PictureLayer? _lastPictureLayer;

	private bool _picturesUnsupported;

	public static readonly StyledProperty<bool> UseSkiaRendererProperty;

	private FormattedText _measureText;

	private string? _currentDirectory;

	private double _charWidth;

	private double _charHeight;

	private string _fontFamilyChain = "monospace";

	private int _bufferSize = 1000;

	private bool _isAlternateBuffer;

	private static readonly Regex UrlRegex;

	private static readonly Cursor HandCursor;

	internal static readonly Cursor IBeamCursor;

	private HoveredUrl? _hoveredLink;

	private Cursor? _savedCursor;

	private bool _cursorOverridden;

	private (int Line, int Col)? _lastHoverProbe;

	private HoveredUrl? _pendingUrlClick;

	private Cursor? _preShapeCursor;

	private bool _shapeOverridden;

	private IPtyConnection? _ptyConnection;

	private volatile bool _atomicUpdate;

	private readonly FrameCapturePool _frameCapture = new FrameCapturePool();

	private long _liveWriteGeneration;

	private long _atomicUpdateStartedAt;

	private volatile bool _bufferWriteInProgress;

	private static readonly TimeSpan AtomicUpdateTimeout;

	private CancellationTokenSource? _processCts;

	private static readonly Encoding Utf8NoBom;

	private int _processExitHandled;

	private readonly object _exitGate = new object();

	private long _sessionId;

	private long _sessionCounter;

	private const int ExitReapGraceMs = 1000;

	private const int ExitReapCeilingMs = 30000;

	private const int ExitReapPollMs = 100;

	private readonly object _terminalLock = new object();

	private DispatcherTimer _cursorBlinkTimer;

	private bool _cursorBlinkOn = true;

	private bool _regridFromLayout;

	private BufferLine? _statusLine;

	private bool _isSelecting;

	private (int Col, int Row)? _pendingSelectionStart;

	private (int X, int Y, MouseEncoding Encoding, MouseEventType Type, MouseTrackingMode Mode, XTerm.Input.MouseButton Button, XTerm.Input.KeyModifiers Modifiers)? _lastReportedMotion;

	private double _wheelResidual;

	private double _wheelResidualApp;

	private bool _followBottom = true;

	private TerminalBuffer? _scrollbackBuffer;

	private volatile bool _autoScroll = true;

	private int? _kbSelAnchor;

	private bool _kbSelWholeInput;

	private int _inputStartRow = -1;

	private int _inputStartCol;

	private int _lastOutputRow = -1;

	private bool _semanticPrompt;

	private bool _inputStartPending;

	private string _pendingReplaceKeys = string.Empty;

	private int _kbSelFocus;

	private TerminalInputMethodClient? _inputMethodClient;

	private static readonly TimeSpan ImeNotifyInterval;

	private volatile bool _imeFocused;

	private long _imeNotifiedAt;

	private int _imeNotifyQueued;

	private int _animationSyncQueued;

	private readonly Guid _instanceId = Guid.NewGuid();

	private bool _suppressCleanupOnDetach;

	private readonly ConditionalWeakTable<TerminalImage, CachedBitmap> _imageBitmaps = new ConditionalWeakTable<TerminalImage, CachedBitmap>();

	private readonly ConditionalWeakTable<SixelStrip, CachedStripBitmap> _stripBitmaps = new ConditionalWeakTable<SixelStrip, CachedStripBitmap>();

	private bool _imageRenderingUnavailable;

	private ColorSnapshot _palette;

	private readonly StringBuilder _runTextBuilder = new StringBuilder(256);

	private readonly List<int> _runBlockCodePoints = new List<int>(256);

	internal static bool GlyphRunFastPathEnabled;

	private TopLevel? _topLevel;

	private double _baseline;

	private readonly Dictionary<(FontStyle Style, FontWeight Weight), GlyphTypeface?> _glyphTypefaces = new Dictionary<(FontStyle, FontWeight), GlyphTypeface>();

	private readonly Dictionary<(FontStyle Style, FontWeight Weight), GlyphTypeface?> _wideFallback = new Dictionary<(FontStyle, FontWeight), GlyphTypeface>();
	// r67.12: 宽字形垂直居中偏移缓存（face+glyph -> offset）
	private readonly Dictionary<(GlyphTypeface Face, ushort Glyph), Vector> _wideVOffsets = new Dictionary<(GlyphTypeface, ushort), Vector>();
	// r67.12（r77）：启动期 CJK 字体预热状态
	private bool _wideWarmupDone;
	private int _wideWarmupRetries;

	private bool _boldIsBright = true;

	private readonly MinimumContrast _minimumContrast = new MinimumContrast();

	public static readonly DirectProperty<TerminalView, bool> IsAlternateBufferProperty;

	public static readonly DirectProperty<TerminalView, int> BufferSizeProperty;

	public static readonly DirectProperty<TerminalView, int> ViewportYProperty;

	public static readonly DirectProperty<TerminalView, int> MaxScrollbackProperty;

	public static readonly DirectProperty<TerminalView, int> ViewportLinesProperty;

	public static readonly DirectProperty<TerminalView, string?> CurrentDirectoryProperty;

	public static readonly FontFamily DefaultFontFamily;

	public static readonly StyledProperty<FontFamily> FontFamilyProperty;

	public static readonly StyledProperty<double> FontSizeProperty;

	public static readonly StyledProperty<FontStyle> FontStyleProperty;

	public static readonly StyledProperty<FontWeight> FontWeightProperty;

	public static readonly StyledProperty<TextDecorationLocation?> TextDecorationsProperty;

	public static readonly StyledProperty<IBrush> ForegroundProperty;

	public static readonly StyledProperty<IBrush> BackgroundProperty;

	public static readonly StyledProperty<IBrush> SelectionBrushProperty;

	public static readonly StyledProperty<string> ProcessProperty;

	public static readonly StyledProperty<IList<string>> ProcessArgsProperty;

	public static readonly StyledProperty<string?> StartingDirectoryProperty;

	public static readonly StyledProperty<Color> CursorColorProperty;

	public static readonly StyledProperty<bool> LigaturesProperty;

	private static readonly FontFeatureCollection LigaturesOff;

	private Action<bool[]?>? _onLigatureAlphabetKnown;

	public static readonly StyledProperty<CursorStyle> CursorStyleProperty;

	public static readonly StyledProperty<bool> CursorBlinkProperty;

	public static readonly StyledProperty<bool> AllowWindowOpsProperty;

	public static readonly StyledProperty<bool> ConvertEolProperty;

	public static readonly StyledProperty<int> CursorBlinkRateProperty;

	public static readonly StyledProperty<bool> ShowCaretOnClickProperty;

	public static readonly StyledProperty<bool> BoldFontProperty;

	public static readonly StyledProperty<bool> AutoCopyOnSelectionProperty;

	public static readonly StyledProperty<ShortcutMode> ShortcutModeProperty;

	public static readonly StyledProperty<bool> SuppressCursorProperty;

	public static readonly StyledProperty<bool> OutputReceivedOnReadTaskProperty;

	private volatile bool _outputOnReadTask;

	public static readonly StyledProperty<bool> AutoScrollToBottomProperty;

	public static readonly StyledProperty<bool> VerbatimCommandLineProperty;

	public const string DefaultTermType = "xterm-256color";

	public const string DefaultColorTerm = "truecolor";

	public static readonly StyledProperty<IDictionary<string, string>?> EnvironmentVariablesProperty;

	public static readonly StyledProperty<TerminalOptions?> OptionsProperty;

	public static readonly StyledProperty<IBrush> SearchHighlightBrushProperty;

	public static readonly StyledProperty<IBrush> SearchCurrentBrushProperty;

	private BufferSearch? _search;

	private int _currentMatchId = -1;

	public static readonly StyledProperty<double> GutterWidthProperty;

	public static readonly StyledProperty<IBrush?> GutterPromptBrushProperty;

	public static readonly StyledProperty<IBrush?> GutterSuccessBrushProperty;

	public static readonly StyledProperty<IBrush?> GutterFailureBrushProperty;

	private const int AnimationTickMilliseconds = 16;

	private DispatcherTimer _animationTimer;

	private readonly Stopwatch _animationClock = new Stopwatch();

	private bool _paletteWalkQueued;

	private bool _disposed;

	private static readonly bool IsMacOS;

	private static readonly TimeSpan WindowInfoPatience;

	private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

	private readonly HashSet<(PhysicalKey Physical, global::Avalonia.Input.Key Logical)> _keysHeld = new HashSet<(PhysicalKey, global::Avalonia.Input.Key)>();

	private bool _win32RecordSentForThisStroke;

	private volatile string? _pendingPointerShape;

	private bool _pointerShapeQueued;

	private readonly List<Action> _pendingHostCallbacks = new List<Action>();

	private bool _hostDrainQueued;

	private Task _clipboardWrites = Task.CompletedTask;

	private const string SupportedClipboardMime = "text/plain";

	private Task? _readLoopTask;

	private bool _externalConnection;

	private readonly List<SizedBlockDraw> _sizedBlockDraws = new List<SizedBlockDraw>();

	private readonly List<FrontImageDraw> _frontImageDraws = new List<FrontImageDraw>();

	private static readonly List<LinePlacement> EmptyPlacements;

	private const int VirtualKeyPacket = 231;

	private const int VirtualKeyCapital = 20;

	private const int VirtualKeyNumLock = 144;

	private const int VirtualKeyScroll = 145;

	public static readonly RoutedEvent<TitleChangedEventArgs> TitleChangedEvent;

	public static readonly RoutedEvent<WindowMovedEventArgs> WindowMovedEvent;

	public static readonly RoutedEvent<WindowResizedEventArgs> WindowResizedEvent;

	public static readonly RoutedEvent<RoutedEventArgs> WindowMinimizedEvent;

	public static readonly RoutedEvent<RoutedEventArgs> WindowMaximizedEvent;

	public static readonly RoutedEvent<RoutedEventArgs> WindowRestoredEvent;

	public static readonly RoutedEvent<RoutedEventArgs> WindowRaisedEvent;

	public static readonly RoutedEvent<RoutedEventArgs> WindowLoweredEvent;

	public static readonly RoutedEvent<RoutedEventArgs> WindowFullscreenedEvent;

	public static readonly RoutedEvent<RoutedEventArgs> BellRangEvent;

	public static readonly RoutedEvent<WindowInfoRequestedEventArgs> WindowInfoRequestedEvent;

	public static readonly RoutedEvent<TerminalNotificationEventArgs> NotificationRequestedEvent;

	public static readonly RoutedEvent<TerminalAttentionEventArgs> AttentionRequestedEvent;

	[Obsolete("Has no effect. Text is drawn by the classic path and pictures by a Skia picture layer wherever Skia is available.")]
	public bool UseSkiaRenderer
	{
		get
		{
			return GetValue(UseSkiaRendererProperty);
		}
		set
		{
			SetValue(UseSkiaRendererProperty, value);
		}
	}

	private double StatusLineHeight
	{
		get
		{
			if (_statusLine != null)
			{
				return _charHeight;
			}
			return 0.0;
		}
	}

	internal (int Row, int Col) InputStart => (Row: _inputStartRow, Col: _inputStartCol);

	private double RenderScale => (_topLevel ?? (_topLevel = TopLevel.GetTopLevel(this)))?.RenderScaling ?? 1.0;

	public bool Ligatures
	{
		get
		{
			return GetValue(LigaturesProperty);
		}
		set
		{
			SetValue(LigaturesProperty, value);
		}
	}

	public bool ShowCaretOnClick
	{
		get
		{
			return GetValue(ShowCaretOnClickProperty);
		}
		set
		{
			SetValue(ShowCaretOnClickProperty, value);
		}
	}

	public bool BoldFont
	{
		get
		{
			return GetValue(BoldFontProperty);
		}
		set
		{
			SetValue(BoldFontProperty, value);
		}
	}

	public bool AutoCopyOnSelection
	{
		get
		{
			return GetValue(AutoCopyOnSelectionProperty);
		}
		set
		{
			SetValue(AutoCopyOnSelectionProperty, value);
		}
	}

	public ShortcutMode ShortcutMode
	{
		get
		{
			return GetValue(ShortcutModeProperty);
		}
		set
		{
			SetValue(ShortcutModeProperty, value);
		}
	}

	public bool SuppressCursor
	{
		get
		{
			return GetValue(SuppressCursorProperty);
		}
		set
		{
			SetValue(SuppressCursorProperty, value);
		}
	}

	public bool OutputReceivedOnReadTask
	{
		get
		{
			return GetValue(OutputReceivedOnReadTaskProperty);
		}
		set
		{
			SetValue(OutputReceivedOnReadTaskProperty, value);
		}
	}

	public bool AutoScrollToBottom
	{
		get
		{
			return GetValue(AutoScrollToBottomProperty);
		}
		set
		{
			SetValue(AutoScrollToBottomProperty, value);
		}
	}

	public bool IsFollowingTail => _followBottom;

	public bool VerbatimCommandLine
	{
		get
		{
			return GetValue(VerbatimCommandLineProperty);
		}
		set
		{
			SetValue(VerbatimCommandLineProperty, value);
		}
	}

	public IDictionary<string, string>? EnvironmentVariables
	{
		get
		{
			return GetValue(EnvironmentVariablesProperty);
		}
		set
		{
			SetValue(EnvironmentVariablesProperty, value);
		}
	}

	public IBrush SearchHighlightBrush
	{
		get
		{
			return GetValue(SearchHighlightBrushProperty);
		}
		set
		{
			SetValue(SearchHighlightBrushProperty, value);
		}
	}

	public IBrush SearchCurrentBrush
	{
		get
		{
			return GetValue(SearchCurrentBrushProperty);
		}
		set
		{
			SetValue(SearchCurrentBrushProperty, value);
		}
	}

	public int SearchHitCount => _search?.Count ?? 0;

	public int SearchCurrentIndex => _search?.CurrentIndex ?? (-1);

	public bool SearchTruncated => _search?.Truncated ?? false;

	public double GutterWidth
	{
		get
		{
			return GetValue(GutterWidthProperty);
		}
		set
		{
			SetValue(GutterWidthProperty, value);
		}
	}

	public IBrush? GutterPromptBrush
	{
		get
		{
			return GetValue(GutterPromptBrushProperty);
		}
		set
		{
			SetValue(GutterPromptBrushProperty, value);
		}
	}

	public IBrush? GutterSuccessBrush
	{
		get
		{
			return GetValue(GutterSuccessBrushProperty);
		}
		set
		{
			SetValue(GutterSuccessBrushProperty, value);
		}
	}

	public IBrush? GutterFailureBrush
	{
		get
		{
			return GetValue(GutterFailureBrushProperty);
		}
		set
		{
			SetValue(GutterFailureBrushProperty, value);
		}
	}

	public IReadOnlyList<VisibleMark> VisibleMarks
	{
		get
		{
			List<VisibleMark> list = new List<VisibleMark>();
			CircularList<BufferLine> lines = _terminal.Buffer.Lines;
			int viewportY = _terminal.Buffer.ViewportY;
			for (int i = 0; i < _terminal.Rows; i++)
			{
				int num = viewportY + i;
				if (num < 0 || num >= lines.Length)
				{
					continue;
				}
				BufferLine bufferLine = lines[num];
				if (bufferLine == null || !bufferLine.HasMarks)
				{
					continue;
				}
				foreach (LineMark mark in bufferLine.Marks)
				{
					list.Add(new VisibleMark(i, num, mark.Kind, mark.ExitCode));
				}
			}
			return list;
		}
	}

	public bool IsAlternateBuffer => _isAlternateBuffer;

	public int BufferSize
	{
		get
		{
			return _bufferSize;
		}
		set
		{
			if (_terminal != null)
			{
				_terminal.Options.Scrollback = value;
			}
			SetAndRaise(BufferSizeProperty, ref _bufferSize, value);
			RequestPaint();
		}
	}

	public int ViewportY
	{
		get
		{
			return _terminal.Buffer.ViewportY;
		}
		set
		{
			int viewportY = _terminal.Buffer.ViewportY;
			_terminal.Buffer.ViewportY = value;
			if (viewportY != _terminal.Buffer.ViewportY)
			{
				_followBottom = _isAlternateBuffer || (_autoScroll && _terminal.Buffer.IsAtBottom);
				RaisePropertyChanged(ViewportYProperty, viewportY, _terminal.Buffer.ViewportY);
				RequestPaint();
			}
		}
	}

	public int MaxScrollback
	{
		get
		{
			int length = _terminal.Buffer.Length;
			int rows = _terminal.Rows;
			return Math.Max(0, length - rows);
		}
	}

	public int ViewportLines => _terminal.Rows;

	public XTerm.Terminal Terminal => _terminal;

	public string CurrentLineText
	{
		get
		{
			if (_terminal == null)
			{
				return string.Empty;
			}
			lock (_terminalLock)
			{
				TerminalBuffer buffer = _terminal.Buffer;
				BufferLine line = buffer.GetLine(buffer.YBase + buffer.Y);
				if (line == null)
				{
					return string.Empty;
				}
				StringBuilder stringBuilder = new StringBuilder(line.Length);
				for (int i = 0; i < line.Length; i++)
				{
					stringBuilder.Append(string.IsNullOrEmpty(line[i].Content) ? " " : line[i].Content);
				}
				return stringBuilder.ToString().TrimEnd();
			}
		}
	}

	public int ExitCode => _ptyConnection?.ExitCode ?? (-1);

	public int Pid => _ptyConnection.Pid;

	public FontFamily FontFamily
	{
		get
		{
			return GetValue(FontFamilyProperty);
		}
		set
		{
			SetValue(FontFamilyProperty, value);
		}
	}

	public double FontSize
	{
		get
		{
			return GetValue(FontSizeProperty);
		}
		set
		{
			SetValue(FontSizeProperty, value);
		}
	}

	public FontStyle FontStyle
	{
		get
		{
			return GetValue(FontStyleProperty);
		}
		set
		{
			SetValue(FontStyleProperty, value);
		}
	}

	public FontWeight FontWeight
	{
		get
		{
			return GetValue(FontWeightProperty);
		}
		set
		{
			SetValue(FontWeightProperty, value);
		}
	}

	public TextDecorationLocation? TextDecorations
	{
		get
		{
			return GetValue(TextDecorationsProperty);
		}
		set
		{
			SetValue(TextDecorationsProperty, value);
		}
	}

	public IBrush Foreground
	{
		get
		{
			return GetValue(ForegroundProperty);
		}
		set
		{
			SetValue(ForegroundProperty, value);
		}
	}

	public IBrush Background
	{
		get
		{
			return GetValue(BackgroundProperty);
		}
		set
		{
			SetValue(BackgroundProperty, value);
		}
	}

	public IBrush SelectionBrush
	{
		get
		{
			return GetValue(SelectionBrushProperty);
		}
		set
		{
			SetValue(SelectionBrushProperty, value);
		}
	}

	public string Process
	{
		get
		{
			return GetValue(ProcessProperty);
		}
		set
		{
			SetValue(ProcessProperty, value);
		}
	}

	public IList<string> ProcessArgs
	{
		get
		{
			return GetValue(ProcessArgsProperty);
		}
		set
		{
			SetValue(ProcessArgsProperty, value);
		}
	}

	public string? StartingDirectory
	{
		get
		{
			return GetValue(StartingDirectoryProperty);
		}
		set
		{
			SetValue(StartingDirectoryProperty, value);
		}
	}

	public string? CurrentDirectory => _currentDirectory;

	public Color CursorColor
	{
		get
		{
			return GetValue(CursorColorProperty);
		}
		set
		{
			SetValue(CursorColorProperty, value);
		}
	}

	public CursorStyle CursorStyle
	{
		get
		{
			return GetValue(CursorStyleProperty);
		}
		set
		{
			SetValue(CursorStyleProperty, value);
		}
	}

	public bool CursorBlink
	{
		get
		{
			return GetValue(CursorBlinkProperty);
		}
		set
		{
			SetValue(CursorBlinkProperty, value);
		}
	}

	public bool AllowWindowOps
	{
		get
		{
			return GetValue(AllowWindowOpsProperty);
		}
		set
		{
			SetValue(AllowWindowOpsProperty, value);
		}
	}

	public bool ConvertEol
	{
		get
		{
			return GetValue(ConvertEolProperty);
		}
		set
		{
			SetValue(ConvertEolProperty, value);
		}
	}

	public int CursorBlinkRate
	{
		get
		{
			return GetValue(CursorBlinkRateProperty);
		}
		set
		{
			SetValue(CursorBlinkRateProperty, value);
		}
	}

	public TerminalOptions? Options
	{
		get
		{
			return GetValue(OptionsProperty);
		}
		set
		{
			SetValue(OptionsProperty, value);
		}
	}

	private bool CanRemoveSelection
	{
		get
		{
			int? kbSelAnchor = _kbSelAnchor;
			if (kbSelAnchor.HasValue && _terminal.Selection.HasSelection && !_terminal.IsAlternateBufferActive)
			{
				return _kbSelAnchor.Value != _kbSelFocus;
			}
			return false;
		}
	}

	private bool CanCut
	{
		get
		{
			if (CanRemoveSelection && TopLevel.GetTopLevel(this)?.Clipboard != null && !string.IsNullOrEmpty(_terminal.Selection.GetSelectionText()))
			{
				return !string.IsNullOrEmpty(BuildKeyboardSelectionDeletion());
			}
			return false;
		}
	}

	public long SessionId
	{
		get
		{
			lock (_exitGate)
			{
				return _sessionId;
			}
		}
	}

	public bool IsLive
	{
		get
		{
			lock (_exitGate)
			{
				return _ptyConnection != null && Volatile.Read(in _processExitHandled) == 0;
			}
		}
	}

	public double CharWidth
	{
		get
		{
			if (_charWidth <= 0.0)
			{
				UpdateTextMetrics();
			}
			return _charWidth;
		}
	}

	public double CharHeight
	{
		get
		{
			if (_charHeight <= 0.0)
			{
				UpdateTextMetrics();
			}
			return _charHeight;
		}
	}

	internal bool CaretHidden
	{
		get
		{
			if (_kbSelWholeInput)
			{
				return _terminal.Selection.HasSelection;
			}
			return false;
		}
	}

	internal (int Column, int AbsoluteRow) CaretPosition
	{
		get
		{
			int? kbSelAnchor = _kbSelAnchor;
			if (kbSelAnchor.HasValue && _terminal.Selection.HasSelection)
			{
				int num = Math.Max(1, _terminal.Cols);
				return (Column: _kbSelFocus % num, AbsoluteRow: _terminal.Buffer.ViewportY + _kbSelFocus / num);
			}
			return (Column: _terminal.Buffer.X, AbsoluteRow: _terminal.Buffer.YBase + _terminal.Buffer.Y);
		}
	}

	internal OutputCounters Counters { get; } = new OutputCounters();

	public event EventHandler<TerminalNotificationEventArgs> NotificationRequested
	{
		add
		{
			AddHandler(NotificationRequestedEvent, value);
		}
		remove
		{
			RemoveHandler(NotificationRequestedEvent, value);
		}
	}

	public event EventHandler<TerminalAttentionEventArgs> AttentionRequested
	{
		add
		{
			AddHandler(AttentionRequestedEvent, value);
		}
		remove
		{
			RemoveHandler(AttentionRequestedEvent, value);
		}
	}

	public event EventHandler? ShellReady;

	public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

	public event EventHandler<TerminalMessageEventArgs>? WritingMessage;

	public event EventHandler<OutputReceivedEventArgs>? OutputReceived;

	public event EventHandler<UrlClickedEventArgs>? UrlClicked;

	public event EventHandler<TitleChangedEventArgs>? TitleChanged;

	public event EventHandler<WindowMovedEventArgs>? WindowMoved;

	public event EventHandler<WindowResizedEventArgs>? WindowResized;

	public event EventHandler? WindowMinimized;

	public event EventHandler? WindowMaximized;

	public event EventHandler? WindowRestored;

	public event EventHandler? WindowRaised;

	public event EventHandler? WindowLowered;

	public event EventHandler? WindowFullscreened;

	public event EventHandler? BellRang;

	public event EventHandler<WindowInfoRequestedEventArgs>? WindowInfoRequested;

	public event EventHandler<string>? InputSent;

	public bool HitTest(Point point)
	{
		return new Rect(Bounds.Size).Contains(point);
	}

	internal void SetStatusLine(BufferLine? line)
	{
		bool flag = _statusLine == null != (line == null);
		_statusLine = line;
		if (flag)
		{
			InvalidateMeasure();
			InvalidateArrange();
		}
		InvalidateVisual();
	}

	private static bool IsBlockCell(BufferCell cell)
	{
		if (cell.ClusterId == 0)
		{
			return BlockGlyphs.IsBlock(cell.CodePoint);
		}
		return false;
	}

	private void ApplyLigatureSetting(FormattedText text)
	{
		if (!Ligatures)
		{
			text.SetFontFeatures(LigaturesOff);
		}
	}

	private bool LigaturesWantShaping(string text, FontStyle style, FontWeight weight)
	{
		if (!Ligatures)
		{
			return false;
		}
		if (LigatureProbe.TryGetAlphabet(new Typeface(FontFamily, style, weight), (bool[]? _) =>
		{
			global::Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateRunCaches);
		}, out bool[] alphabet) && alphabet != null)
		{
			return LigatureProbe.ContainsCandidate(text, alphabet);
		}
		return false;
	}

	private void OnTerminalOscReceived(object? sender, TerminalEvents.OscReceivedEventArgs e)
	{
		if (e.Code == 133 && !string.IsNullOrEmpty(e.Data))
		{
			char c = e.Data[0];
			if ((c == 'B' || c == 'I') ? true : false)
			{
				_inputStartRow = _terminal.Buffer.YBase + _terminal.Buffer.Y;
				_inputStartCol = _terminal.Buffer.X;
				_inputStartPending = false;
				_semanticPrompt = true;
			}
		}
	}

	private void OnBufferTrimmed(int count)
	{
		if (!_followBottom && count > 0)
		{
			int viewportY = _terminal.Buffer.ViewportY;
			if (viewportY > 0)
			{
				_terminal.Buffer.ViewportY = Math.Max(0, viewportY - count);
			}
		}
	}

	private void WriteOwnLine(string text)
	{
		lock (_terminalLock)
		{
			int viewportY = _terminal.Buffer.ViewportY;
			_followBottom = _isAlternateBuffer || (_autoScroll && _terminal.Buffer.IsAtBottom);
			Interlocked.Increment(ref _liveWriteGeneration);
			_terminal.WriteLine(text);
			if (!_isAlternateBuffer)
			{
				if (_followBottom)
				{
					_terminal.Buffer.ScrollToBottom();
				}
				else if (!_autoScroll && _terminal.Buffer.ViewportY != viewportY)
				{
					_terminal.Buffer.ViewportY = Math.Min(viewportY, MaxScrollback);
				}
			}
		}
		RequestPaint();
	}

	static TerminalView()
	{
		UseSkiaRendererProperty = AvaloniaProperty.Register<TerminalView, bool>("UseSkiaRenderer", defaultValue: false);
		UrlRegex = new Regex("https?://[^\\s<>\"'`]+", RegexOptions.Compiled);
		HandCursor = new Cursor(StandardCursorType.Hand);
		IBeamCursor = new Cursor(StandardCursorType.Ibeam);
		AtomicUpdateTimeout = TimeSpan.FromMilliseconds(150L);
		Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
		ImeNotifyInterval = TimeSpan.FromMilliseconds(100L);
		GlyphRunFastPathEnabled = true;
		IsAlternateBufferProperty = AvaloniaProperty.RegisterDirect("IsAlternateBuffer", (TerminalView o) => o.IsAlternateBuffer, null, unsetValue: false);
		BufferSizeProperty = AvaloniaProperty.RegisterDirect("BufferSize", (TerminalView o) => o._bufferSize, (TerminalView o, int v) =>
		{
			o._bufferSize = v;
		}, 0);
		ViewportYProperty = AvaloniaProperty.RegisterDirect("ViewportY", (TerminalView o) => o.ViewportY, (TerminalView o, int v) =>
		{
			o.ViewportY = v;
		}, 0);
		MaxScrollbackProperty = AvaloniaProperty.RegisterDirect("MaxScrollback", (TerminalView o) => o.MaxScrollback, null, 0);
		ViewportLinesProperty = AvaloniaProperty.RegisterDirect("ViewportLines", (TerminalView o) => o.ViewportLines, null, 0);
		CurrentDirectoryProperty = AvaloniaProperty.RegisterDirect("CurrentDirectory", (TerminalView o) => o.CurrentDirectory);
		DefaultFontFamily = new FontFamily("Cascadia Mono,Cascadia Code,Consolas,Menlo,DejaVu Sans Mono,Liberation Mono,Courier New,Segoe UI Emoji,Apple Color Emoji,Noto Color Emoji,monospace");
		FontFamilyProperty = AvaloniaProperty.Register<TerminalView, FontFamily>("FontFamily", DefaultFontFamily);
		FontSizeProperty = AvaloniaProperty.Register<TerminalView, double>("FontSize", 12.0);
		FontStyleProperty = AvaloniaProperty.Register<TerminalView, FontStyle>("FontStyle", FontStyle.Normal);
		FontWeightProperty = AvaloniaProperty.Register<TerminalView, FontWeight>("FontWeight", FontWeight.Normal);
		TextDecorationsProperty = AvaloniaProperty.Register<TerminalView, TextDecorationLocation?>("TextDecorations");
		ForegroundProperty = AvaloniaProperty.Register<TerminalView, IBrush>("Foreground", Brushes.White);
		BackgroundProperty = AvaloniaProperty.Register<TerminalView, IBrush>("Background", Brushes.Black);
		SelectionBrushProperty = AvaloniaProperty.Register<TerminalView, IBrush>("SelectionBrush", new SolidColorBrush(Color.FromArgb(128, 0, 120, 215)));
		ProcessProperty = AvaloniaProperty.Register<TerminalView, string>("Process", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash");
		ProcessArgsProperty = AvaloniaProperty.Register<TerminalView, IList<string>>("ProcessArgs", Array.Empty<string>());
		StartingDirectoryProperty = AvaloniaProperty.Register<TerminalView, string>("StartingDirectory", Environment.CurrentDirectory);
		CursorColorProperty = AvaloniaProperty.Register<TerminalView, Color>("CursorColor", Colors.White);
		LigaturesProperty = AvaloniaProperty.Register<TerminalView, bool>("Ligatures", defaultValue: false);
		LigaturesOff = new FontFeatureCollection
		{
			new FontFeature
			{
				Tag = "calt",
				Value = 0
			},
			new FontFeature
			{
				Tag = "liga",
				Value = 0
			}
		};
		CursorStyleProperty = AvaloniaProperty.Register<TerminalView, CursorStyle>("CursorStyle", CursorStyle.Bar);
		CursorBlinkProperty = AvaloniaProperty.Register<TerminalView, bool>("CursorBlink", defaultValue: true);
		AllowWindowOpsProperty = AvaloniaProperty.Register<TerminalView, bool>("AllowWindowOps", defaultValue: false);
		ConvertEolProperty = AvaloniaProperty.Register<TerminalView, bool>("ConvertEol", defaultValue: false);
		CursorBlinkRateProperty = AvaloniaProperty.Register<TerminalView, int>("CursorBlinkRate", 530);
		ShowCaretOnClickProperty = AvaloniaProperty.Register<TerminalView, bool>("ShowCaretOnClick", defaultValue: false);
		BoldFontProperty = AvaloniaProperty.Register<TerminalView, bool>("BoldFont", defaultValue: true);
		AutoCopyOnSelectionProperty = AvaloniaProperty.Register<TerminalView, bool>("AutoCopyOnSelection", defaultValue: true);
		ShortcutModeProperty = AvaloniaProperty.Register<TerminalView, ShortcutMode>("ShortcutMode", ShortcutMode.Terminal);
		SuppressCursorProperty = AvaloniaProperty.Register<TerminalView, bool>("SuppressCursor", defaultValue: false);
		OutputReceivedOnReadTaskProperty = AvaloniaProperty.Register<TerminalView, bool>("OutputReceivedOnReadTask", defaultValue: false);
		AutoScrollToBottomProperty = AvaloniaProperty.Register<TerminalView, bool>("AutoScrollToBottom", defaultValue: true);
		VerbatimCommandLineProperty = AvaloniaProperty.Register<TerminalView, bool>("VerbatimCommandLine", defaultValue: false);
		EnvironmentVariablesProperty = AvaloniaProperty.Register<TerminalView, IDictionary<string, string>>("EnvironmentVariables");
		OptionsProperty = AvaloniaProperty.Register<TerminalView, TerminalOptions>("Options");
		SearchHighlightBrushProperty = AvaloniaProperty.Register<TerminalView, IBrush>("SearchHighlightBrush", new SolidColorBrush(Color.FromArgb(90, 240, 180, 41)));
		SearchCurrentBrushProperty = AvaloniaProperty.Register<TerminalView, IBrush>("SearchCurrentBrush", new SolidColorBrush(Color.FromArgb(160, 240, 180, 41)));
		GutterWidthProperty = AvaloniaProperty.Register<TerminalView, double>("GutterWidth", 0.0);
		GutterPromptBrushProperty = AvaloniaProperty.Register<TerminalView, IBrush>("GutterPromptBrush");
		GutterSuccessBrushProperty = AvaloniaProperty.Register<TerminalView, IBrush>("GutterSuccessBrush");
		GutterFailureBrushProperty = AvaloniaProperty.Register<TerminalView, IBrush>("GutterFailureBrush");
		IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
		WindowInfoPatience = TimeSpan.FromMilliseconds(250L);
		EmptyPlacements = new List<LinePlacement>();
		TitleChangedEvent = RoutedEvent.Register<TerminalView, TitleChangedEventArgs>("TitleChanged", RoutingStrategies.Bubble);
		WindowMovedEvent = RoutedEvent.Register<TerminalView, WindowMovedEventArgs>("WindowMoved", RoutingStrategies.Bubble);
		WindowResizedEvent = RoutedEvent.Register<TerminalView, WindowResizedEventArgs>("WindowResized", RoutingStrategies.Bubble);
		WindowMinimizedEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("WindowMinimized", RoutingStrategies.Bubble);
		WindowMaximizedEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("WindowMaximized", RoutingStrategies.Bubble);
		WindowRestoredEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("WindowRestored", RoutingStrategies.Bubble);
		WindowRaisedEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("WindowRaised", RoutingStrategies.Bubble);
		WindowLoweredEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("WindowLowered", RoutingStrategies.Bubble);
		WindowFullscreenedEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("WindowFullscreened", RoutingStrategies.Bubble);
		BellRangEvent = RoutedEvent.Register<TerminalView, RoutedEventArgs>("BellRang", RoutingStrategies.Bubble);
		WindowInfoRequestedEvent = RoutedEvent.Register<TerminalView, WindowInfoRequestedEventArgs>("WindowInfoRequested", RoutingStrategies.Bubble);
		NotificationRequestedEvent = RoutedEvent.Register<TerminalView, TerminalNotificationEventArgs>("NotificationRequested", RoutingStrategies.Bubble);
		AttentionRequestedEvent = RoutedEvent.Register<TerminalView, TerminalAttentionEventArgs>("AttentionRequested", RoutingStrategies.Bubble);
		Visual.AffectsRender<TerminalView>(new AvaloniaProperty[20]
		{
			FontFamilyProperty, FontSizeProperty, FontStyleProperty, FontWeightProperty, TextDecorationsProperty, ForegroundProperty, BackgroundProperty, SelectionBrushProperty, BufferSizeProperty, ViewportYProperty,
			CursorColorProperty, CursorStyleProperty, CursorBlinkProperty, SuppressCursorProperty, GutterWidthProperty, GutterPromptBrushProperty, GutterSuccessBrushProperty, GutterFailureBrushProperty, SearchHighlightBrushProperty, SearchCurrentBrushProperty
		});
		Layoutable.AffectsMeasure<TerminalView>(new AvaloniaProperty[6] { GutterWidthProperty, FontFamilyProperty, FontSizeProperty, FontStyleProperty, FontWeightProperty, BufferSizeProperty });
		InputElement.FocusableProperty.OverrideDefaultValue<TerminalView>(defaultValue: true);
	}

	public TerminalView()
	{
		Focusable = true;
		Loaded += OnLoaded;
		Unloaded += OnUnloaded;
		TextInputMethodClientRequested += OnTextInputMethodClientRequested;
	}

	protected override void OnInitialized()
	{
		TerminalOptions terminalOptions = Options ?? new TerminalOptions();
		terminalOptions.CursorStyle = CursorStyle;
		terminalOptions.CursorBlink = CursorBlink;
		terminalOptions.CursorBlinkRate = CursorBlinkRate;
		_outputOnReadTask = OutputReceivedOnReadTask;
		terminalOptions.Scrollback = _bufferSize;
		terminalOptions.ConvertEol = ConvertEol;
		SeedThemeFromBrushes(terminalOptions.Theme);
		terminalOptions.PointerShapesEnabled = true;
		terminalOptions.WindowOptions.GetCellSizePixels = true;
		terminalOptions.WindowOptions.GetWinSizeChars = true;
		terminalOptions.WindowOptions.GetWinSizePixels = true;
		terminalOptions.WindowOptions.GetScreenSizePixels = true;
		if (AllowWindowOps)
		{
			EnableWindowManipulation(terminalOptions.WindowOptions);
		}
		_terminal = new XTerm.Terminal(terminalOptions);
		SetCurrentValue(OptionsProperty, _terminal.Options);
		_palette = _terminal.Colors.Take();
		_terminal.Colors.ColorChanged += OnTerminalColorChanged;
		_scrollbackBuffer = _terminal.Buffer;
		_terminal.OscReceived += OnTerminalOscReceived;
		SubscribeTerminalEvents();
		_cursorBlinkTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(CursorBlinkRate)
		};
		_cursorBlinkTimer.Tick += OnCursorBlinkTick;
		_animationTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(16L)
		};
		_animationTimer.Tick += OnAnimationTick;
		_inputMethodClient = new TerminalInputMethodClient(this);
	}

	private void SyncAnimationClock()
	{
		bool flag = _terminal.HasRunningAnimations();
		if (flag != _animationTimer.IsEnabled)
		{
			if (flag)
			{
				_animationClock.Restart();
				_animationTimer.Start();
			}
			else
			{
				_animationTimer.Stop();
			}
		}
	}

	private void OnAnimationTick(object? sender, EventArgs e)
	{
		TimeSpan elapsed = _animationClock.Elapsed;
		_animationClock.Restart();
		if (_terminal.AdvanceAnimations(elapsed))
		{
			RequestPaint();
		}
		SyncAnimationClock();
	}

	private void OnTerminalDirectoryChanged(object? sender, TerminalEvents.DirectoryChangeEventArgs e)
	{
		string directory = e.Directory;
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			string currentDirectory = _currentDirectory;
			_currentDirectory = directory;
			RaisePropertyChanged(CurrentDirectoryProperty, currentDirectory, _currentDirectory);
		});
	}

	public void Kill()
	{
		_ptyConnection?.Kill();
	}

	public void ClearScreen()
	{
		if (_terminal != null)
		{
			lock (_terminalLock)
			{
				Interlocked.Increment(ref _liveWriteGeneration);
				_terminal.Write("\u001b[H\u001b[2J\u001b[3J");
				_terminal.Buffer.ScrollToBottom();
			}
			RequestPaint();
		}
	}

	public async Task<bool> CutAsync()
	{
		if (!_terminal.Selection.HasSelection)
		{
			return false;
		}
		if (!CanRemoveSelection)
		{
			return false;
		}
		if (!(await CopyAsync().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		string text = TakeKeyboardSelectionDeletion();
		if (text.Length == 0)
		{
			return false;
		}
		await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	private void OnSynchronizedOutputChanged(object? sender, TerminalEvents.SynchronizedOutputEventArgs e)
	{
		if (e.Active)
		{
			BeginAtomicUpdate();
			return;
		}
		_frameCapture.Publish(_terminal, Interlocked.Read(in _liveWriteGeneration));
		EndAtomicUpdate();
	}

	private void BeginAtomicUpdate()
	{
		_atomicUpdateStartedAt = Stopwatch.GetTimestamp();
		_atomicUpdate = true;
	}

	private void EndAtomicUpdate()
	{
		if (_atomicUpdate)
		{
			_atomicUpdate = false;
			RequestPaint();
		}
	}

	private static void EnableWindowManipulation(WindowOptions windowOptions)
	{
		windowOptions.SetWinPosition = true;
		windowOptions.SetWinSizePixels = true;
		windowOptions.SetWinSizeChars = true;
		windowOptions.RaiseWin = true;
		windowOptions.LowerWin = true;
		windowOptions.RefreshWin = true;
		windowOptions.RestoreWin = true;
		windowOptions.MaximizeWin = true;
		windowOptions.MinimizeWin = true;
		windowOptions.FullscreenWin = true;
	}

	private void SeedThemeFromBrushes(ThemeOptions theme)
	{
		if (Foreground is ISolidColorBrush solidColorBrush)
		{
			theme.Foreground = ToHex(solidColorBrush.Color);
		}
		if (Background is ISolidColorBrush solidColorBrush2)
		{
			theme.Background = ToHex(solidColorBrush2.Color);
		}
		static string ToHex(Color c)
		{
			return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
		}
	}

	private void OnTerminalColorChanged(object? sender, EventArgs e)
	{
		lock (_pendingHostCallbacks)
		{
			if (_paletteWalkQueued)
			{
				return;
			}
			_paletteWalkQueued = true;
		}
		PostToHost(() =>
		{
			lock (_pendingHostCallbacks)
			{
				_paletteWalkQueued = false;
			}
			InvalidateRunCaches();
		});
	}

	protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
	{
		if (change.Property == AutoScrollToBottomProperty)
		{
			_autoScroll = change.GetNewValue<bool>();
		}
		if (change.Property == OptionsProperty && _terminal != null && change.NewValue != _terminal.Options)
		{
			SetCurrentValue(OptionsProperty, _terminal.Options);
			return;
		}
		base.OnPropertyChanged(change);
		if (_terminal == null || _cursorBlinkTimer == null)
		{
			return;
		}
		if (change.Property == ForegroundProperty || change.Property == BackgroundProperty)
		{
			SyncPaletteToBrushes();
			InvalidateRunCaches();
		}
		else if (change.Property == ConvertEolProperty)
		{
			_terminal.Options.ConvertEol = (bool)change.NewValue;
		}
		else if (change.Property == LigaturesProperty)
		{
			InvalidateRunCaches();
		}
		else if (change.Property == AllowWindowOpsProperty && (bool)change.NewValue)
		{
			EnableWindowManipulation(_terminal.Options.WindowOptions);
		}
		else if (change.Property == CursorStyleProperty)
		{
			_terminal.Options.CursorStyle = (CursorStyle)change.NewValue;
		}
		else if (change.Property == CursorBlinkProperty)
		{
			bool flag = (bool)change.NewValue;
			_terminal.Options.CursorBlink = flag;
			if (flag && IsFocused)
			{
				_cursorBlinkTimer.Start();
				return;
			}
			_cursorBlinkTimer.Stop();
			_cursorBlinkOn = true;
		}
		else if (change.Property == OutputReceivedOnReadTaskProperty)
		{
			_outputOnReadTask = (bool)change.NewValue;
		}
		else if (change.Property == CursorBlinkRateProperty)
		{
			int num = (int)change.NewValue;
			_terminal.Options.CursorBlinkRate = num;
			_cursorBlinkTimer.Interval = TimeSpan.FromMilliseconds((num > 0) ? num : 530);
		}
	}

	private async void OnLoaded(object? sender, RoutedEventArgs e)
	{
		if (_ptyConnection == null && !string.IsNullOrEmpty(Process))
		{
			await LaunchProcess();
		}
		if (CursorBlink)
		{
			_cursorBlinkTimer.Start();
		}
		SyncAnimationClock();
	}

	private void OnUnloaded(object? sender, RoutedEventArgs e)
	{
		_search?.Dispose();
		_search = null;
		_currentMatchId = -1;
		_cursorBlinkTimer.Stop();
		_animationTimer.Stop();
		_isSelecting = false;
		_pendingSelectionStart = null;
	}

	public void BeginReparent()
	{
		_suppressCleanupOnDetach = true;
	}

	public void EndReparent()
	{
		_suppressCleanupOnDetach = false;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_pictures.Clear();
			_lastPictureLayer = null;
			UnsubscribeTerminalEvents();
			if (_terminal != null)
			{
				_terminal.OscReceived -= OnTerminalOscReceived;
				_terminal.Colors.ColorChanged -= OnTerminalColorChanged;
			}
			_cursorBlinkTimer?.Stop();
			_animationTimer?.Stop();
			_atomicUpdate = false;
			CleanupProcess();
			_terminal?.Dispose();
			GC.SuppressFinalize(this);
		}
	}

	private void SubscribeTerminalEvents()
	{
		if (_terminal != null)
		{
			if (_scrollbackBuffer != null)
			{
				_scrollbackBuffer.Trimmed += OnBufferTrimmed;
			}
			_terminal.DataReceived += OnTerminalDataReceived;
			_terminal.BufferChanged += OnTerminalBufferChanged;
			_terminal.CursorStyleChanged += OnTerminalCursorStyleChanged;
			_terminal.TitleChanged += OnTerminalTitleChanged;
			_terminal.StatusLineChanged += OnTerminalStatusLineChanged;
			_terminal.SynchronizedOutputChanged += OnSynchronizedOutputChanged;
			_terminal.WindowMoved += OnTerminalWindowMoved;
			_terminal.Resized += OnTerminalResized;
			_terminal.WindowResized += OnTerminalWindowResized;
			_terminal.WindowMinimized += OnTerminalWindowMinimized;
			_terminal.WindowMaximized += OnTerminalWindowMaximized;
			_terminal.WindowRestored += OnTerminalWindowRestored;
			_terminal.WindowRaised += OnTerminalWindowRaised;
			_terminal.WindowLowered += OnTerminalWindowLowered;
			_terminal.WindowFullscreened += OnTerminalWindowFullscreened;
			_terminal.BellRang += OnTerminalBellRang;
			_terminal.DirectoryChanged += OnTerminalDirectoryChanged;
			_terminal.ClipboardWriteRequested += OnTerminalClipboardWriteRequested;
			_terminal.ClipboardReadRequested += OnTerminalClipboardReadRequested;
			_terminal.NotificationReceived += OnTerminalNotificationReceived;
			_terminal.AttentionRequested += OnTerminalAttentionRequested;
			_terminal.PointerShapeChanged += OnTerminalPointerShapeChanged;
			_terminal.WindowInfoRequested += OnTerminalWindowInfoRequested;
		}
	}

	private void UnsubscribeTerminalEvents()
	{
		if (_terminal != null)
		{
			if (_scrollbackBuffer != null)
			{
				_scrollbackBuffer.Trimmed -= OnBufferTrimmed;
			}
			_terminal.DataReceived -= OnTerminalDataReceived;
			_terminal.BufferChanged -= OnTerminalBufferChanged;
			_terminal.CursorStyleChanged -= OnTerminalCursorStyleChanged;
			_terminal.TitleChanged -= OnTerminalTitleChanged;
			_terminal.StatusLineChanged -= OnTerminalStatusLineChanged;
			_terminal.SynchronizedOutputChanged -= OnSynchronizedOutputChanged;
			_terminal.WindowMoved -= OnTerminalWindowMoved;
			_terminal.Resized -= OnTerminalResized;
			_terminal.WindowResized -= OnTerminalWindowResized;
			_terminal.WindowMinimized -= OnTerminalWindowMinimized;
			_terminal.WindowMaximized -= OnTerminalWindowMaximized;
			_terminal.WindowRestored -= OnTerminalWindowRestored;
			_terminal.WindowRaised -= OnTerminalWindowRaised;
			_terminal.WindowLowered -= OnTerminalWindowLowered;
			_terminal.WindowFullscreened -= OnTerminalWindowFullscreened;
			_terminal.BellRang -= OnTerminalBellRang;
			_terminal.DirectoryChanged -= OnTerminalDirectoryChanged;
			_terminal.ClipboardWriteRequested -= OnTerminalClipboardWriteRequested;
			_terminal.ClipboardReadRequested -= OnTerminalClipboardReadRequested;
			_terminal.NotificationReceived -= OnTerminalNotificationReceived;
			_terminal.AttentionRequested -= OnTerminalAttentionRequested;
			_terminal.PointerShapeChanged -= OnTerminalPointerShapeChanged;
			_terminal.WindowInfoRequested -= OnTerminalWindowInfoRequested;
		}
	}

	protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
	{
		base.OnAttachedToLogicalTree(e);
		if (!_disposed && _terminal != null)
		{
			UnsubscribeTerminalEvents();
			SubscribeTerminalEvents();
		}
	}

	private void OnCursorBlinkTick(object? sender, EventArgs e)
	{
		if (!CursorBlink || !IsFocused)
		{
			return;
		}
		_cursorBlinkOn = !_cursorBlinkOn;
		int viewportY = _terminal.Buffer.ViewportY;
		for (int i = 0; i < _terminal.Rows; i++)
		{
			BufferLine line = _terminal.Buffer.GetLine(viewportY + i);
			if (line == null)
			{
				continue;
			}
			for (int j = 0; j < line.Length; j++)
			{
				if (line[j].Attributes.IsBlink())
				{
					line.Cache = null;
					break;
				}
			}
		}
		BufferLine statusLine = _statusLine;
		if (statusLine != null)
		{
			for (int k = 0; k < statusLine.Length; k++)
			{
				if (statusLine[k].Attributes.IsBlink())
				{
					statusLine.Cache = null;
					break;
				}
			}
		}
		RequestPaint();
	}

	protected override async void OnKeyDown(KeyEventArgs e)
	{
		_win32RecordSentForThisStroke = false;
		if (!IsFocused)
		{
			base.OnKeyDown(e);
			return;
		}
		if (_ptyConnection == null)
		{
			base.OnKeyDown(e);
			return;
		}
		bool flag = ShortcutMode != ShortcutMode.None;
		if (_processExitHandled != 0)
		{
			if (flag && e.Key == global::Avalonia.Input.Key.C && (e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Control || e.KeyModifiers == (global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift) || (IsMacOS && e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Meta)) && _terminal.Selection.HasSelection)
			{
				e.Handled = true;
				await CopyAsync();
				RequestPaint();
			}
			else
			{
				base.OnKeyDown(e);
			}
			return;
		}
		try
		{
			if (flag && IsMacOS && e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Meta)
			{
				if (e.Key == global::Avalonia.Input.Key.C)
				{
					e.Handled = true;
					if (_terminal.Selection.HasSelection)
					{
						await CopyAsync();
						RequestPaint();
					}
					return;
				}
				if (e.Key == global::Avalonia.Input.Key.V)
				{
					e.Handled = true;
					await PasteAsync();
					return;
				}
			}
			if (flag && IsMacOS && e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Meta)
			{
				if (e.Key == global::Avalonia.Input.Key.X && CanCut)
				{
					e.Handled = true;
					await CutAsync().ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
				if (e.Key == global::Avalonia.Input.Key.A)
				{
					e.Handled = true;
					await SelectInputAsync().ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
			}
			bool flag2;
			if (ShortcutMode == ShortcutMode.Desktop && !IsMacOS && !_terminal.IsAlternateBufferActive)
			{
				if (e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Control)
				{
					switch (e.Key)
					{
					case global::Avalonia.Input.Key.A:
						e.Handled = true;
						await SelectInputAsync().ConfigureAwait(continueOnCapturedContext: false);
						return;
					case global::Avalonia.Input.Key.V:
						e.Handled = true;
						await PasteAsync().ConfigureAwait(continueOnCapturedContext: false);
						return;
					case global::Avalonia.Input.Key.X:
						if (CanCut)
						{
							e.Handled = true;
							await CutAsync().ConfigureAwait(continueOnCapturedContext: false);
							return;
						}
						break;
					}
				}
				flag2 = e.KeyModifiers == (global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift);
				if (flag2)
				{
					global::Avalonia.Input.Key key = e.Key;
					bool flag3 = ((key == global::Avalonia.Input.Key.V || key == global::Avalonia.Input.Key.X) ? true : false);
					flag2 = flag3;
				}
				if (flag2)
				{
					string text = _terminal.GenerateCharInput((e.Key == global::Avalonia.Input.Key.V) ? 'v' : 'x', XTerm.Input.KeyModifiers.Control);
					if (!string.IsNullOrEmpty(text))
					{
						e.Handled = true;
						await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
						return;
					}
				}
			}
			if (flag && e.Key == global::Avalonia.Input.Key.C && e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Control && _terminal.Selection.HasSelection)
			{
				e.Handled = true;
				await CopyAsync();
				RequestPaint();
				return;
			}
			if (flag && e.Key == global::Avalonia.Input.Key.C && e.KeyModifiers == (global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift) && _terminal.Selection.HasSelection)
			{
				e.Handled = true;
				await CopyAsync();
				RequestPaint();
				return;
			}
			if (!IsModifierKey(e.Key))
			{
				FollowTail();
			}
			if (TryExtendKeyboardSelection(e))
			{
				e.Handled = true;
				return;
			}
			if (!IsModifierKey(e.Key))
			{
				bool flag4 = (e.KeyModifiers & (global::Avalonia.Input.KeyModifiers.Alt | global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Meta)) == 0;
				bool flag5 = flag4 && TryGetPrintableChar(e, out var _);
				flag2 = flag4;
				if (flag2)
				{
					global::Avalonia.Input.Key key = e.Key;
					bool flag3 = ((key == global::Avalonia.Input.Key.Back || key == global::Avalonia.Input.Key.Delete) ? true : false);
					flag2 = flag3;
				}
				bool flag6 = flag2;
				if (flag5 | flag6)
				{
					NoteInputStart();
				}
				_pendingReplaceKeys = ((flag5 | flag6) ? TakeKeyboardSelectionDeletion() : string.Empty);
				if (_pendingReplaceKeys.Length == 0)
				{
					_kbSelAnchor = null;
					_kbSelWholeInput = false;
					if (_terminal.Selection.HasSelection)
					{
						_terminal.Selection.ClearSelection();
						RequestPaint();
					}
				}
			}
			if (flag && e.Key == global::Avalonia.Input.Key.V && e.KeyModifiers == (global::Avalonia.Input.KeyModifiers.Control | global::Avalonia.Input.KeyModifiers.Shift))
			{
				e.Handled = true;
				await PasteAsync();
				return;
			}
			flag2 = IsMacOS && e.KeyModifiers == global::Avalonia.Input.KeyModifiers.Meta;
			if (flag2)
			{
				global::Avalonia.Input.Key key = e.Key;
				bool flag3 = ((key == global::Avalonia.Input.Key.Left || key == global::Avalonia.Input.Key.Right) ? true : false);
				flag2 = flag3;
			}
			if (flag2 && !_terminal.KittyKeyboardActive)
			{
				e.Handled = true;
				await SendToPtyAsync((e.Key == global::Avalonia.Input.Key.Left) ? "\u001b[H" : "\u001b[F").ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				if ((e.KeyModifiers & global::Avalonia.Input.KeyModifiers.Meta) != 0 && !_terminal.KittyKeyboardActive)
				{
					return;
				}
				global::Avalonia.Input.Key key = e.Key;
				flag2 = ((key == global::Avalonia.Input.Key.Left || key == global::Avalonia.Input.Key.Right) ? true : false);
				bool flag3 = flag2;
				if (flag3)
				{
					global::Avalonia.Input.KeyModifiers keyModifiers = e.KeyModifiers;
					bool flag7 = (uint)(keyModifiers - 1) <= 1u;
					flag3 = flag7;
				}
				if (flag3 && !_terminal.Win32InputMode && !_terminal.KittyKeyboardActive && !_terminal.IsAlternateBufferActive)
				{
					e.Handled = true;
					await SendToPtyAsync((e.Key == global::Avalonia.Input.Key.Left) ? "\u001bb" : "\u001bf").ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
				XTerm.Input.KeyModifiers modifiers = ConvertAvaloniaModifiers(e.KeyModifiers);
				bool flag8 = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
				bool flag9 = e.Key == global::Avalonia.Input.Key.Escape;
				if (_terminal.Win32InputMode || ((flag8 & flag9) && !_terminal.KittyKeyboardActive))
				{
					string text2 = GenerateWin32InputSequence(e, isKeyDown: true);
					if (!string.IsNullOrEmpty(text2))
					{
						e.Handled = true;
						string pendingReplaceKeys = _pendingReplaceKeys;
						_pendingReplaceKeys = string.Empty;
						flag3 = pendingReplaceKeys.Length > 0;
						if (flag3)
						{
							key = e.Key;
							flag2 = ((key == global::Avalonia.Input.Key.Back || key == global::Avalonia.Input.Key.Delete) ? true : false);
							flag3 = flag2;
						}
						string data = (flag3 ? pendingReplaceKeys : (pendingReplaceKeys + text2));
						_win32RecordSentForThisStroke = true;
						await SendToPtyAsync(data).ConfigureAwait(continueOnCapturedContext: false);
						return;
					}
				}
				if (await TrySendKittyKeyAsync(e, KittyKeyboardEventType.Press, _pendingReplaceKeys).ConfigureAwait(continueOnCapturedContext: false))
				{
					_pendingReplaceKeys = string.Empty;
					return;
				}
				XTerm.Input.Key? key2 = ConvertAvaloniaKeyToXTermKey(e.Key);
				char composed;
				char character3;
				if (key2.HasValue)
				{
					string pendingReplaceKeys2 = _pendingReplaceKeys;
					_pendingReplaceKeys = string.Empty;
					flag3 = pendingReplaceKeys2.Length > 0;
					if (flag3)
					{
						key = e.Key;
						flag2 = ((key == global::Avalonia.Input.Key.Back || key == global::Avalonia.Input.Key.Delete) ? true : false);
						flag3 = flag2;
					}
					if (flag3)
					{
						e.Handled = true;
						await SendToPtyAsync(pendingReplaceKeys2).ConfigureAwait(continueOnCapturedContext: false);
						return;
					}
					string text3 = _terminal.GenerateKeyInput(key2.Value, modifiers);
					if (!string.IsNullOrEmpty(text3))
					{
						e.Handled = true;
						await SendToPtyAsync(text3).ConfigureAwait(continueOnCapturedContext: false);
					}
				}
				else if (IsAltGrComposed(e, out composed))
				{
					e.Handled = true;
					string pendingReplaceKeys3 = _pendingReplaceKeys;
					_pendingReplaceKeys = string.Empty;
					await SendToPtyAsync(pendingReplaceKeys3 + composed).ConfigureAwait(continueOnCapturedContext: false);
				}
				else if ((modifiers & (XTerm.Input.KeyModifiers.Alt | XTerm.Input.KeyModifiers.Control)) != 0)
				{
					if (TryGetPrintableChar(e, out var character2))
					{
						string text4 = _terminal.GenerateCharInput(character2, modifiers);
						if (!string.IsNullOrEmpty(text4))
						{
							e.Handled = true;
							await SendToPtyAsync(text4).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
				}
				else if (TryGetPrintableChar(e, out character3))
				{
					e.Handled = true;
					string pendingReplaceKeys4 = _pendingReplaceKeys;
					_pendingReplaceKeys = string.Empty;
					await SendToPtyAsync(pendingReplaceKeys4 + character3).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private string EncodeSyntheticKey(global::Avalonia.Input.Key avaloniaKey, XTerm.Input.Key xtermKey, string kittyName)
	{
		if (_terminal.Win32InputMode)
		{
			int num = ConvertAvaloniaKeyToVirtualKey(avaloniaKey);
			if (num == 0)
			{
				return string.Empty;
			}
			int unicodeChar = ((avaloniaKey == global::Avalonia.Input.Key.Back) ? 8 : 0);
			Win32ControlKeyState win32ControlKeyState = GetWin32ControlKeyState(global::Avalonia.Input.KeyModifiers.None, avaloniaKey);
			return Win32Record(num, 0, unicodeChar, isKeyDown: true, win32ControlKeyState) + Win32Record(num, 0, unicodeChar, isKeyDown: false, win32ControlKeyState);
		}
		if (_terminal.KittyKeyboardActive)
		{
			KeyEvent ev = new KeyEvent
			{
				Key = kittyName,
				Code = kittyName
			};
			string? obj = _terminal.GenerateKittyKeyInput(ev) ?? _terminal.GenerateKeyInput(xtermKey);
			string text = _terminal.GenerateKittyKeyInput(ev, KittyKeyboardEventType.Release);
			return obj + text;
		}
		return _terminal.GenerateKeyInput(xtermKey);
	}

	private int InputEndBoundary(int cols, int lastBoundary)
	{
		int num = InputStartBoundary(cols, lastBoundary);
		for (int num2 = lastBoundary - 1; num2 >= num; num2--)
		{
			int num3 = num2 / cols;
			int num4 = num2 % cols;
			BufferLine line = _terminal.Buffer.GetLine(_terminal.Buffer.ViewportY + num3);
			if (line != null && num4 < line.Length)
			{
				BufferCell bufferCell = line[num4];
				if (!string.IsNullOrWhiteSpace(bufferCell.Content))
				{
					return Math.Min(num2 + Math.Max(1, bufferCell.Width), lastBoundary);
				}
			}
		}
		return num;
	}

	protected override async void OnTextInput(TextInputEventArgs e)
	{
		if (!IsFocused)
		{
			base.OnTextInput(e);
			return;
		}
		if (_ptyConnection == null || string.IsNullOrEmpty(e.Text) || _processExitHandled != 0)
		{
			base.OnTextInput(e);
			return;
		}
		if (_terminal.Win32InputMode)
		{
			bool win32RecordSentForThisStroke = _win32RecordSentForThisStroke;
			_win32RecordSentForThisStroke = false;
			e.Handled = true;
			if (!win32RecordSentForThisStroke)
			{
				await SendToPtyAsync(Win32TextRecords(e.Text)).ConfigureAwait(continueOnCapturedContext: false);
			}
			return;
		}
		NoteInputStart();
		string text = ((_pendingReplaceKeys.Length > 0) ? _pendingReplaceKeys : TakeKeyboardSelectionDeletion());
		_pendingReplaceKeys = string.Empty;
		if (text.Length == 0)
		{
			_kbSelAnchor = null;
			_kbSelWholeInput = false;
			if (_terminal.Selection.HasSelection)
			{
				_terminal.Selection.ClearSelection();
				RequestPaint();
			}
		}
		FollowTail();
		try
		{
			e.Handled = true;
			await SendToPtyAsync(text + e.Text).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception)
		{
		}
	}

	private void OnTerminalBufferChanged(object? sender, TerminalEvents.BufferChangedEventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			bool isAlternateBuffer = _isAlternateBuffer;
			_isAlternateBuffer = e.Buffer == BufferType.Alternate;
			if (isAlternateBuffer != _isAlternateBuffer)
			{
				if (_terminal.Selection.HasSelection)
				{
					_terminal.Selection.ClearSelection();
				}
				_kbSelAnchor = null;
				_kbSelWholeInput = false;
				_isSelecting = false;
				_pendingSelectionStart = null;
				_lastReportedMotion = null;
				RaisePropertyChanged(IsAlternateBufferProperty, isAlternateBuffer, _isAlternateBuffer);
			}
			RaisePropertyChanged(MaxScrollbackProperty, 0, MaxScrollback);
			RaisePropertyChanged(ViewportLinesProperty, 0, ViewportLines);
			RaisePropertyChanged(ViewportYProperty, 0, ViewportY);
			RequestPaint();
		});
	}

	private void OnTerminalCursorStyleChanged(object? sender, TerminalEvents.CursorStyleChangedEventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (!object.Equals(CursorStyle, e.Style))
			{
				SetValue(CursorStyleProperty, e.Style);
			}
			if (!object.Equals(CursorBlink, e.Blink))
			{
				SetValue(CursorBlinkProperty, e.Blink);
			}
			RequestPaint();
		});
	}

	private async void OnTerminalDataReceived(object? sender, TerminalEvents.DataEventArgs e)
	{
		await SendToPtyAsync(e.Data).ConfigureAwait(continueOnCapturedContext: false);
	}

	private static int CountChar(string text, char c)
	{
		int num = 0;
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] == c)
			{
				num++;
			}
		}
		return num;
	}

	private void PostToHost(Action callback)
	{
		bool flag;
		lock (_pendingHostCallbacks)
		{
			_pendingHostCallbacks.Add(callback);
			flag = !_hostDrainQueued;
			_hostDrainQueued = true;
		}
		if (flag)
		{
			global::Avalonia.Threading.Dispatcher.UIThread.Post(DrainHostCallbacks);
		}
	}

	private void DrainHostCallbacks()
	{
		Action[] array;
		lock (_pendingHostCallbacks)
		{
			array = _pendingHostCallbacks.ToArray();
			_pendingHostCallbacks.Clear();
			_hostDrainQueued = false;
		}
		Action[] array2 = array;
		foreach (Action action in array2)
		{
			try
			{
				action();
			}
			catch (Exception)
			{
			}
		}
	}

	public void Refresh()
	{
		if (_terminal == null)
		{
			return;
		}
		UpdateTextMetrics();
		for (int i = 0; i < _terminal.Buffer.Length; i++)
		{
			BufferLine line = _terminal.Buffer.GetLine(i);
			if (line != null)
			{
				line.Cache = null;
			}
		}
		InvalidateMeasure();
		InvalidateArrange();
		InvalidateVisual();
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		if (_charWidth > 0.0)
		{
			int num = Math.Max(1, (int)((finalSize.Width - Math.Max(0.0, GutterWidth)) / _charWidth));
			int num2 = Math.Max(1, (int)((finalSize.Height - StatusLineHeight) / _charHeight));
			if (num != _terminal.Cols || num2 != _terminal.Rows)
			{
				lock (_terminalLock)
				{
					_regridFromLayout = true;
					try
					{
						_terminal.Resize(num, num2);
					}
					finally
					{
						_regridFromLayout = false;
					}
				}
				_ptyConnection?.Resize(num, num2);
				RaisePropertyChanged(ViewportLinesProperty, 0, ViewportLines);
			}
		}
		return finalSize;
	}

	private List<CachedTextRun> BuildLineRuns(BufferLine line, double startYPos, double rowHeight, out bool hasSizedRuns)
	{
		List<CachedTextRun> list = new List<CachedTextRun>();
		List<LinePlacement> list2 = OrderedPlacements(line);
		int i = 0;
		List<LinePlacement> list3 = ((list2.Count > 0) ? new List<LinePlacement>(list2.Count) : EmptyPlacements);
		for (; i < list2.Count && list2[i].ZIndex < 0; i++)
		{
			AppendImageRun(line, list2[i], list, list3);
			list3.Add(list2[i]);
		}
		hasSizedRuns = line.HasSizedRuns;
		int j = 0;
		while (j < _terminal.Cols && j < line.Length)
		{
			if (hasSizedRuns && line.TryGetSizedRunAt(j, out var run) && run.Covers(j))
			{
				_sizedBlockDraws.Add(new SizedBlockDraw(line, run, startYPos, rowHeight));
				j = run.EndColumn;
				continue;
			}
			BufferCell bufferCell = line[j];
			string text = string.Empty;
			int cellCount = 0;
			int num = 0;
			int[] array = null;
			bool flag = CoveredByBackdrop(list3, j, j + Math.Max(1, bufferCell.Width));
			if (bufferCell.Width == 0)
			{
				j++;
				continue;
			}
			if (bufferCell.Width == 1)
			{
				StringBuilder runTextBuilder = _runTextBuilder;
				runTextBuilder.Clear();
				cellCount = 0;
				num = j;
				bool flag2 = IsBlockCell(bufferCell);
				List<int> runBlockCodePoints = _runBlockCodePoints;
				runBlockCodePoints.Clear();
				BufferCell cell;
				for (; j < line.Length && j < _terminal.Cols; j += cell.Width)
				{
					cell = line[j];
					if (cell.Width != 1 || cell.Attributes != bufferCell.Attributes || CoveredByBackdrop(list3, j, j + 1) != flag || (hasSizedRuns && line.TryGetSizedRunAt(j, out var _)) || IsBlockCell(cell) != flag2)
					{
						break;
					}
					if (flag2)
					{
						runBlockCodePoints.Add(cell.CodePoint);
					}
					if (cell.ClusterId == 0 && cell.CodePoint > 0 && cell.CodePoint < 65536)
					{
						runTextBuilder.Append((char)cell.CodePoint);
					}
					else
					{
						runTextBuilder.Append(cell.Content);
					}
					cellCount += cell.Width;
				}
				text = runTextBuilder.ToString();
				if (flag2)
				{
					array = runBlockCodePoints.ToArray();
				}
			}
			else if (bufferCell.Width == 2)
			{
				text = bufferCell.Content;
				cellCount = bufferCell.Width;
				num = j;
				j += bufferCell.Width;
			}
			if (array == null)
			{
				text = GraphemeRuns.AbsorbJoinedCells(line, _terminal.Cols, bufferCell, text, ref j, ref cellCount);
			}
			IBrush brush = bufferCell.GetBackgroundBrush(_palette, Background);
			IBrush brush2 = bufferCell.GetForegroundBrush(_palette, Foreground, BoldFont && _boldIsBright);
			bool flag3 = false;
			if (bufferCell.Attributes.IsInverse())
			{
				IBrush brush3 = brush;
				IBrush brush4 = brush2;
				flag3 = !flag3;
				brush = brush4;
				brush2 = brush3;
			}
			if (_terminal.ReverseVideo)
			{
				IBrush brush5 = brush;
				IBrush brush6 = brush2;
				flag3 = !flag3;
				brush = brush6;
				brush2 = brush5;
			}
			if (_minimumContrast.Active && !flag && !MinimumContrast.IsExemptRun(text) && brush2 is ISolidColorBrush solidColorBrush && brush is ISolidColorBrush solidColorBrush2 && BufferCellExtensions.IsFullyOpaque(brush))
			{
				Color color = _minimumContrast.Apply(solidColorBrush.Color, solidColorBrush2.Color);
				if (color != solidColorBrush.Color)
				{
					brush2 = new SolidColorBrush(color, solidColorBrush.Opacity);
				}
			}
			brush2 = bufferCell.ApplyConceal(brush2);
			brush2 = bufferCell.ApplyBlinkPhase(brush2, _cursorBlinkOn);
			FontStyle fontStyle = bufferCell.GetFontStyle();
			FontWeight weight = (BoldFont ? bufferCell.GetFontWeight() : FontWeight.Normal);
			TextDecorationCollection textDecorations = bufferCell.GetTextDecorations();
			UnderlineStyle underlineStyle = bufferCell.Attributes.GetUnderlineStyle();
			IBrush brush7 = null;
			IBrush brush8;
			if (underlineStyle != UnderlineStyle.None)
			{
				Color? underlineColor = bufferCell.GetUnderlineColor(_palette);
				IBrush brush9;
				if (underlineColor.HasValue)
				{
					Color valueOrDefault = underlineColor.GetValueOrDefault();
					brush8 = new ImmutableSolidColorBrush(valueOrDefault);
					brush9 = brush8;
				}
				else
				{
					brush9 = brush2;
				}
				brush7 = brush9;
				brush7 = bufferCell.ApplyBlinkPhase(brush7, _cursorBlinkOn);
			}
			GlyphRun glyphRun = null;
			FormattedText formattedText = null;
			if (array == null && (!IsBlankRun(text) || underlineStyle != UnderlineStyle.None || textDecorations != null))
			{
				glyphRun = ((textDecorations == null && !LigaturesWantShaping(text, fontStyle, weight)) ? TryBuildGlyphRun(text, cellCount, fontStyle, weight) : null);
				if (glyphRun == null)
				{
					formattedText = new FormattedText(typeface: new Typeface(FontFamily, fontStyle, weight), textToFormat: text, culture: CultureInfo.CurrentCulture, flowDirection: FlowDirection.LeftToRight, emSize: FontSize, foreground: brush2);
					ApplyLigatureSetting(formattedText);
					if (textDecorations != null)
					{
						formattedText.SetTextDecorations(textDecorations);
					}
				}
			}
			IBrush brush10 = ((flag3 || _terminal.ReverseVideo || bufferCell.GetBackgroundColor(_palette).HasValue) ? brush : null);
			if ((brush10 != null) & flag)
			{
				brush10 = null;
			}
			FormattedText text2 = formattedText;
			int startX = num;
			int cellCount2 = cellCount;
			IBrush background = brush10;
			GlyphRun glyphs = glyphRun;
			brush8 = brush2;
			UnderlineStyle underlineStyle2 = underlineStyle;
			IBrush underlineBrush = brush7;
			int[] blocks = array;
			CachedTextRun item = new CachedTextRun(text2, startX, cellCount2, background, null, null, underlineStyle2, underlineBrush, glyphs, brush8, blocks);
			list.Add(item);
		}
		SixelStrip sixel = line.Sixel;
		if (sixel != null)
		{
			int num2 = Math.Min(sixel.PaintedColumns, Math.Min(line.Length, _terminal.Cols));
			if (num2 > 0)
			{
				SixelStrip strip = sixel;
				list.Add(new CachedTextRun(null, 0, num2, null, null, null, UnderlineStyle.None, null, null, null, null, strip));
			}
		}
		for (; i < list2.Count; i++)
		{
			AppendImageRun(line, list2[i], list, list3);
			list3.Add(list2[i]);
		}
		return list;
	}

	public static void AddTitleChangedHandler(Interactive target, EventHandler<TitleChangedEventArgs> handler)
	{
		target.AddHandler(TitleChangedEvent, handler);
	}

	public static void RemoveTitleChangedHandler(Interactive target, EventHandler<TitleChangedEventArgs> handler)
	{
		target.RemoveHandler(TitleChangedEvent, handler);
	}

	public static void AddWindowMovedHandler(Interactive target, EventHandler<WindowMovedEventArgs> handler)
	{
		target.AddHandler(WindowMovedEvent, handler);
	}

	public static void RemoveWindowMovedHandler(Interactive target, EventHandler<WindowMovedEventArgs> handler)
	{
		target.RemoveHandler(WindowMovedEvent, handler);
	}

	public static void AddWindowResizedHandler(Interactive target, EventHandler<WindowResizedEventArgs> handler)
	{
		target.AddHandler(WindowResizedEvent, handler);
	}

	public static void RemoveWindowResizedHandler(Interactive target, EventHandler<WindowResizedEventArgs> handler)
	{
		target.RemoveHandler(WindowResizedEvent, handler);
	}

	public static void AddWindowMinimizedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(WindowMinimizedEvent, handler);
	}

	public static void RemoveWindowMinimizedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(WindowMinimizedEvent, handler);
	}

	public static void AddWindowMaximizedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(WindowMaximizedEvent, handler);
	}

	public static void RemoveWindowMaximizedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(WindowMaximizedEvent, handler);
	}

	public static void AddWindowRestoredHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(WindowRestoredEvent, handler);
	}

	public static void RemoveWindowRestoredHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(WindowRestoredEvent, handler);
	}

	public static void AddWindowRaisedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(WindowRaisedEvent, handler);
	}

	public static void RemoveWindowRaisedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(WindowRaisedEvent, handler);
	}

	public static void AddWindowLoweredHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(WindowLoweredEvent, handler);
	}

	public static void RemoveWindowLoweredHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(WindowLoweredEvent, handler);
	}

	public static void AddWindowFullscreenedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(WindowFullscreenedEvent, handler);
	}

	public static void RemoveWindowFullscreenedHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(WindowFullscreenedEvent, handler);
	}

	public static void AddBellRangHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.AddHandler(BellRangEvent, handler);
	}

	public static void RemoveBellRangHandler(Interactive target, EventHandler<RoutedEventArgs> handler)
	{
		target.RemoveHandler(BellRangEvent, handler);
	}

	public static void AddWindowInfoRequestedHandler(Interactive target, EventHandler<WindowInfoRequestedEventArgs> handler)
	{
		target.AddHandler(WindowInfoRequestedEvent, handler);
	}

	public static void RemoveWindowInfoRequestedHandler(Interactive target, EventHandler<WindowInfoRequestedEventArgs> handler)
	{
		target.RemoveHandler(WindowInfoRequestedEvent, handler);
	}

	private void OnTerminalTitleChanged(object? sender, TerminalEvents.TitleChangeEventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			TitleChangedEventArgs e2 = new TitleChangedEventArgs(e.Title)
			{
				RoutedEvent = TitleChangedEvent
			};
			RaiseEvent(e2);
			TitleChanged?.Invoke(this, e2);
		});
	}

	private void OnTerminalStatusLineChanged(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			XTerm.Terminal terminal = _terminal;
			if (terminal != null)
			{
				SetStatusLine((terminal.StatusDisplayType == 2) ? terminal.StatusLine : null);
			}
		});
	}

	private void OnTerminalWindowMoved(object? sender, TerminalEvents.WindowMovedEventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				WindowMovedEventArgs e2 = new WindowMovedEventArgs(e.X, e.Y)
				{
					RoutedEvent = WindowMovedEvent
				};
				RaiseEvent(e2);
				WindowMoved?.Invoke(this, e2);
			}
		});
	}

	private void OnTerminalResized(object? sender, TerminalEvents.ResizeEventArgs e)
	{
		if (_regridFromLayout)
		{
			return;
		}
		int cols = e.Cols;
		int rows = e.Rows;
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps && !(_charWidth <= 0.0) && !(_charHeight <= 0.0))
			{
				TopLevel topLevel = TopLevel.GetTopLevel(this);
				double num = ((topLevel != null) ? Math.Max(0.0, topLevel.Bounds.Width - Bounds.Width) : 0.0);
				double num2 = ((topLevel != null) ? Math.Max(0.0, topLevel.Bounds.Height - Bounds.Height) : 0.0);
				int width = (int)Math.Ceiling((double)cols * _charWidth + Math.Max(0.0, GutterWidth) + num);
				int height = (int)Math.Ceiling((double)rows * _charHeight + num2);
				WindowResizedEventArgs e2 = new WindowResizedEventArgs(width, height)
				{
					RoutedEvent = WindowResizedEvent
				};
				RaiseEvent(e2);
				WindowResized?.Invoke(this, e2);
			}
		});
	}

	private void OnTerminalWindowResized(object? sender, TerminalEvents.WindowResizedEventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				WindowResizedEventArgs e2 = new WindowResizedEventArgs(e.Width, e.Height)
				{
					RoutedEvent = WindowResizedEvent
				};
				RaiseEvent(e2);
				WindowResized?.Invoke(this, e2);
			}
		});
	}

	private void OnTerminalWindowMinimized(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				RoutedEventArgs e2 = new RoutedEventArgs(WindowMinimizedEvent);
				RaiseEvent(e2);
				WindowMinimized?.Invoke(this, EventArgs.Empty);
			}
		});
	}

	private void OnTerminalWindowMaximized(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				RoutedEventArgs e2 = new RoutedEventArgs(WindowMaximizedEvent);
				RaiseEvent(e2);
				WindowMaximized?.Invoke(this, EventArgs.Empty);
			}
		});
	}

	private void OnTerminalWindowRestored(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				RoutedEventArgs e2 = new RoutedEventArgs(WindowRestoredEvent);
				RaiseEvent(e2);
				WindowRestored?.Invoke(this, EventArgs.Empty);
			}
		});
	}

	private void OnTerminalWindowRaised(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				RoutedEventArgs e2 = new RoutedEventArgs(WindowRaisedEvent);
				RaiseEvent(e2);
				WindowRaised?.Invoke(this, EventArgs.Empty);
			}
		});
	}

	private void OnTerminalWindowLowered(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				RoutedEventArgs e2 = new RoutedEventArgs(WindowLoweredEvent);
				RaiseEvent(e2);
				WindowLowered?.Invoke(this, EventArgs.Empty);
			}
		});
	}

	private void OnTerminalWindowFullscreened(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (AllowWindowOps)
			{
				RoutedEventArgs e2 = new RoutedEventArgs(WindowFullscreenedEvent);
				RaiseEvent(e2);
				WindowFullscreened?.Invoke(this, EventArgs.Empty);
			}
		});
	}

	private void OnTerminalBellRang(object? sender, EventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			RoutedEventArgs e2 = new RoutedEventArgs(BellRangEvent);
			RaiseEvent(e2);
			BellRang?.Invoke(this, EventArgs.Empty);
		});
	}

	private void OnTerminalWindowInfoRequested(object? sender, TerminalEvents.WindowInfoRequestedEventArgs e)
	{
		switch (e.Request)
		{
		case WindowInfoRequest.CellSizePixels:
			e.CellWidth = _terminal.Options.CellWidthPixels;
			e.CellHeight = _terminal.Options.CellHeightPixels;
			e.Handled = e.CellWidth > 0 && e.CellHeight > 0;
			return;
		case WindowInfoRequest.SizePixels:
		case WindowInfoRequest.ScreenSizePixels:
			e.WidthPixels = _terminal.Cols * _terminal.Options.CellWidthPixels;
			e.HeightPixels = _terminal.Rows * _terminal.Options.CellHeightPixels;
			e.Handled = e.WidthPixels > 0 && e.HeightPixels > 0;
			return;
		}
		if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
		{
			int claim = 0;
			if (!global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
			{
				if (Interlocked.Exchange(ref claim, 1) == 0)
				{
					AnswerWindowInfo(e);
				}
			}).GetTask().Wait(WindowInfoPatience))
			{
				Interlocked.Exchange(ref claim, 1);
			}
		}
		else
		{
			AnswerWindowInfo(e);
		}
	}

	private void OnTerminalNotificationReceived(object? sender, TerminalEvents.NotificationEventArgs e)
	{
		PostToHost(() =>
		{
			RaiseEvent(new TerminalNotificationEventArgs(NotificationRequestedEvent, e));
		});
	}

	private void OnTerminalAttentionRequested(object? sender, TerminalEvents.AttentionRequestedEventArgs e)
	{
		PostToHost(() =>
		{
			RaiseEvent(new TerminalAttentionEventArgs(AttentionRequestedEvent, e.Action));
		});
	}

	private void NoteInputStart()
	{
		if (!_semanticPrompt && _inputStartPending && _terminal != null)
		{
			_inputStartRow = _terminal.Buffer.YBase + _terminal.Buffer.Y;
			_inputStartCol = _terminal.Buffer.X;
			_inputStartPending = false;
		}
	}

	public Task SendInputAsync(string text, CancellationToken cancellationToken = default(CancellationToken))
	{
		return SendToPtyAsync(text, cancellationToken);
	}

	public async Task PasteAsync()
	{
		if (_ptyConnection == null)
		{
			return;
		}
		IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard == null)
		{
			return;
		}
		if (_terminal.PasteNotificationMode)
		{
			TerminalPaste paste = await BuildPasteAsync(clipboard);
			if (paste != null)
			{
				string text = TakeKeyboardSelectionDeletion();
				if (text.Length > 0)
				{
					await SendToPtyAsync(text);
				}
				_terminal.Paste(paste);
			}
			return;
		}
		string text2 = await clipboard.TryGetTextAsync();
		if (!string.IsNullOrEmpty(text2))
		{
			if (_terminal.BracketedPasteMode)
			{
				text2 = "\u001b[200~" + text2 + "\u001b[201~";
			}
			await SendToPtyAsync(TakeKeyboardSelectionDeletion() + text2);
		}
	}

	private static async Task<TerminalPaste?> BuildPasteAsync(IClipboard clipboard)
	{
		List<string> mimes = new List<string>();
		Dictionary<string, byte[]> data = new Dictionary<string, byte[]>();
		try
		{
			foreach (DataFormat format in await clipboard.GetDataFormatsAsync())
			{
				if (format.Identifier.Contains('/'))
				{
					DataFormat<byte[]> format2 = DataFormat.CreateBytesPlatformFormat(format.Identifier);
					byte[] array = await clipboard.TryGetValueAsync(format2);
					if (array != null)
					{
						mimes.Add(format.Identifier);
						data[format.Identifier] = array;
					}
				}
			}
		}
		catch
		{
		}
		string text = await clipboard.TryGetTextAsync();
		if (!string.IsNullOrEmpty(text) && !data.ContainsKey("text/plain"))
		{
			mimes.Add("text/plain");
			data["text/plain"] = Encoding.UTF8.GetBytes(text);
		}
		try
		{
			IStorageItem[] array2 = await clipboard.TryGetFilesAsync();
			if (array2 != null && array2.Length > 0 && !data.ContainsKey("text/uri-list"))
			{
				string s = string.Join('\n', array2.Select((IStorageItem f) => f.Path.AbsoluteUri));
				mimes.Add("text/uri-list");
				data["text/uri-list"] = Encoding.UTF8.GetBytes(s);
			}
		}
		catch
		{
		}
		return (mimes.Count == 0) ? null : new TerminalPaste(mimes, (string mime) => (!data.TryGetValue(mime, out byte[] value)) ? null : value);
	}

	public async Task<bool> CopyAsync()
	{
		if (!_terminal.Selection.HasSelection)
		{
			return false;
		}
		IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard == null)
		{
			return false;
		}
		string text = _terminal.Selection.GetSelectionText();
		if (!string.IsNullOrEmpty(text))
		{
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				text = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
			}
			await clipboard.SetTextAsync(text);
			return true;
		}
		return false;
	}

	private async Task CopyOnSelectionAsync()
	{
		try
		{
			await CopyAsync();
		}
		catch (Exception)
		{
		}
	}

	private static bool IsModifierKey(global::Avalonia.Input.Key key)
	{
		if (key == global::Avalonia.Input.Key.CapsLock || (uint)(key - 70) <= 1u || (uint)(key - 114) <= 7u)
		{
			return true;
		}
		return false;
	}

	private string BuildKeyboardSelectionDeletion()
	{
		int? kbSelAnchor = _kbSelAnchor;
		if (!kbSelAnchor.HasValue || !_terminal.Selection.HasSelection)
		{
			return string.Empty;
		}
		if (_terminal.IsAlternateBufferActive)
		{
			return string.Empty;
		}
		int value = _kbSelAnchor.Value;
		int num = Math.Abs(value - _kbSelFocus);
		if (num == 0)
		{
			return string.Empty;
		}
		string text = EncodeSyntheticKey(global::Avalonia.Input.Key.Back, XTerm.Input.Key.Backspace, "Backspace");
		if (string.IsNullOrEmpty(text))
		{
			return string.Empty;
		}
		if (_kbSelFocus < value)
		{
			return string.Concat(Enumerable.Repeat(text, num));
		}
		string text2 = EncodeSyntheticKey(global::Avalonia.Input.Key.Right, XTerm.Input.Key.RightArrow, "ArrowRight");
		if (string.IsNullOrEmpty(text2))
		{
			return string.Empty;
		}
		return string.Concat(string.Concat(Enumerable.Repeat(text2, num)), string.Concat(Enumerable.Repeat(text, num)));
	}

	private string TakeKeyboardSelectionDeletion()
	{
		string text = BuildKeyboardSelectionDeletion();
		if (text.Length == 0)
		{
			return string.Empty;
		}
		_kbSelAnchor = null;
		_kbSelWholeInput = false;
		_terminal.Selection.ClearSelection();
		RequestPaint();
		return text;
	}

	private bool TryExtendKeyboardSelection(KeyEventArgs e)
	{
		global::Avalonia.Input.Key key = e.Key;
		if ((uint)(key - 21) > 5u)
		{
			return false;
		}
		NoteInputStart();
		key = e.Key;
		bool flag = ((key == global::Avalonia.Input.Key.Left || key == global::Avalonia.Input.Key.Right) ? true : false);
		bool flag2 = flag;
		if (flag2)
		{
			global::Avalonia.Input.KeyModifiers keyModifiers = e.KeyModifiers;
			bool flag3 = (uint)(keyModifiers - 5) <= 1u;
			flag2 = flag3;
		}
		bool flag4 = flag2;
		bool flag5 = IsMacOS && e.KeyModifiers == (global::Avalonia.Input.KeyModifiers.Shift | global::Avalonia.Input.KeyModifiers.Meta);
		if (e.KeyModifiers != global::Avalonia.Input.KeyModifiers.Shift && !flag4 && !flag5)
		{
			return false;
		}
		if (_terminal.IsAlternateBufferActive)
		{
			return false;
		}
		int num = Math.Max(1, _terminal.Cols);
		int num2 = num * Math.Max(1, _terminal.Rows);
		int? kbSelAnchor = _kbSelAnchor;
		if (!kbSelAnchor.HasValue || !_terminal.Selection.HasSelection)
		{
			int num3 = Math.Clamp(Math.Clamp(_terminal.Buffer.YBase + _terminal.Buffer.Y - _terminal.Buffer.ViewportY, 0, Math.Max(0, _terminal.Rows - 1)) * num + _terminal.Buffer.X, 0, num2);
			_kbSelAnchor = num3;
			_kbSelFocus = num3;
		}
		global::Avalonia.Input.Key key2 = e.Key;
		if (flag5)
		{
			switch (key2)
			{
			case global::Avalonia.Input.Key.Left:
				key2 = global::Avalonia.Input.Key.Home;
				break;
			case global::Avalonia.Input.Key.Right:
				key2 = global::Avalonia.Input.Key.End;
				break;
			default:
				return false;
			}
		}
		int kbSelFocus = _kbSelFocus;
		switch (key2)
		{
		case global::Avalonia.Input.Key.Left:
			kbSelFocus = (flag4 ? WordBoundary(kbSelFocus, -1, num, num2) : (kbSelFocus - 1));
			break;
		case global::Avalonia.Input.Key.Right:
			kbSelFocus = (flag4 ? WordBoundary(kbSelFocus, 1, num, num2) : (kbSelFocus + 1));
			break;
		case global::Avalonia.Input.Key.Up:
			kbSelFocus -= num;
			break;
		case global::Avalonia.Input.Key.Down:
			kbSelFocus += num;
			break;
		case global::Avalonia.Input.Key.Home:
		{
			int num4 = InputStartBoundary(num, num2);
			kbSelFocus = ((num4 / num == kbSelFocus / num) ? num4 : (kbSelFocus - kbSelFocus % num));
			break;
		}
		case global::Avalonia.Input.Key.End:
			kbSelFocus = LineEndBoundary(kbSelFocus, num);
			break;
		default:
			return false;
		}
		int num5 = InputStartBoundary(num, num2);
		int max = Math.Max(num5, InputEndBoundary(num, num2));
		_kbSelFocus = Math.Clamp(kbSelFocus, num5, max);
		_kbSelWholeInput = false;
		int value = _kbSelAnchor.Value;
		if (_kbSelFocus == value)
		{
			_terminal.Selection.ClearSelection();
			_kbSelAnchor = null;
			_kbSelWholeInput = false;
		}
		else
		{
			int num6 = Math.Min(value, _kbSelFocus);
			int num7 = Math.Max(value, _kbSelFocus) - 1;
			_terminal.Selection.StartSelection(num6 % num, num6 / num);
			_terminal.Selection.UpdateSelection(num7 % num, num7 / num);
			_terminal.Selection.EndSelection();
		}
		RequestPaint();
		return true;
	}

	private async Task<bool> SelectInputAsync()
	{
		NoteInputStart();
		int cols = Math.Max(1, _terminal.Cols);
		int lastBoundary = cols * Math.Max(1, _terminal.Rows);
		int start = InputStartBoundary(cols, lastBoundary);
		int end = InputEndBoundary(cols, lastBoundary);
		if (end <= start)
		{
			_terminal.Selection.ClearSelection();
			_kbSelAnchor = null;
			_kbSelWholeInput = false;
			RequestPaint();
			return false;
		}
		string text = _terminal.GenerateKeyInput(XTerm.Input.Key.End);
		if (!string.IsNullOrEmpty(text))
		{
			await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
		}
		_kbSelAnchor = end;
		_kbSelFocus = start;
		_kbSelWholeInput = true;
		_terminal.Selection.StartSelection(start % cols, start / cols);
		_terminal.Selection.UpdateSelection((end - 1) % cols, (end - 1) / cols);
		_terminal.Selection.EndSelection();
		RequestPaint();
		return true;
	}

	private int InputStartBoundary(int cols, int lastBoundary)
	{
		if (_inputStartRow < 0)
		{
			return 0;
		}
		int num = _inputStartRow - _terminal.Buffer.ViewportY;
		if (num < 0 || num >= _terminal.Rows)
		{
			return 0;
		}
		return Math.Clamp(num * cols + _inputStartCol, 0, lastBoundary);
	}

	protected override async void OnKeyUp(KeyEventArgs e)
	{
		if (!IsFocused)
		{
			base.OnKeyUp(e);
			return;
		}
		if (_ptyConnection == null || _processExitHandled != 0)
		{
			base.OnKeyUp(e);
			return;
		}
		try
		{
			bool flag = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
			bool flag2 = e.Key == global::Avalonia.Input.Key.Escape;
			if (_terminal.Win32InputMode || ((flag & flag2) && !_terminal.KittyKeyboardActive))
			{
				string text = GenerateWin32InputSequence(e, isKeyDown: false);
				if (!string.IsNullOrEmpty(text))
				{
					e.Handled = true;
					await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else
			{
				await TrySendKittyKeyAsync(e, KittyKeyboardEventType.Release).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			_win32RecordSentForThisStroke = false;
		}
	}

	protected override async void OnGotFocus(FocusChangedEventArgs e)
	{
		base.OnGotFocus(e);
		_imeFocused = true;
		_cursorBlinkOn = true;
		if (CursorBlink)
		{
			_cursorBlinkTimer.Start();
		}
		if (_ptyConnection != null && _terminal.SendFocusEvents)
		{
			string text = _terminal.GenerateFocusEvent(focused: true);
			if (!string.IsNullOrEmpty(text))
			{
				await SendToPtyAsync(text);
			}
		}
		RequestPaint();
	}

	protected override async void OnLostFocus(FocusChangedEventArgs e)
	{
		base.OnLostFocus(e);
		_imeFocused = false;
		_keysHeld.Clear();
		_cursorBlinkTimer.Stop();
		_cursorBlinkOn = true;
		_inputMethodClient?.ClearPreeditText();
		if (_ptyConnection != null && _terminal.SendFocusEvents)
		{
			string text = _terminal.GenerateFocusEvent(focused: false);
			if (!string.IsNullOrEmpty(text))
			{
				await SendToPtyAsync(text);
			}
		}
		RequestPaint();
	}

	private void OnTextInputMethodClientRequested(object? sender, TextInputMethodClientRequestedEventArgs e)
	{
		e.Client = _inputMethodClient;
	}

	private XTerm.Input.Key? ConvertAvaloniaKeyToXTermKey(global::Avalonia.Input.Key key)
	{
		return key switch
		{
			global::Avalonia.Input.Key.Return => (XTerm.Input.Key?)XTerm.Input.Key.Enter, 
			global::Avalonia.Input.Key.Back => XTerm.Input.Key.Backspace, 
			global::Avalonia.Input.Key.Tab => XTerm.Input.Key.Tab, 
			global::Avalonia.Input.Key.Escape => XTerm.Input.Key.Escape, 
			global::Avalonia.Input.Key.Up => XTerm.Input.Key.UpArrow, 
			global::Avalonia.Input.Key.Down => XTerm.Input.Key.DownArrow, 
			global::Avalonia.Input.Key.Left => XTerm.Input.Key.LeftArrow, 
			global::Avalonia.Input.Key.Right => XTerm.Input.Key.RightArrow, 
			global::Avalonia.Input.Key.Home => XTerm.Input.Key.Home, 
			global::Avalonia.Input.Key.End => XTerm.Input.Key.End, 
			global::Avalonia.Input.Key.PageUp => XTerm.Input.Key.PageUp, 
			global::Avalonia.Input.Key.PageDown => XTerm.Input.Key.PageDown, 
			global::Avalonia.Input.Key.Insert => XTerm.Input.Key.Insert, 
			global::Avalonia.Input.Key.Delete => XTerm.Input.Key.Delete, 
			global::Avalonia.Input.Key.F1 => XTerm.Input.Key.F1, 
			global::Avalonia.Input.Key.F2 => XTerm.Input.Key.F2, 
			global::Avalonia.Input.Key.F3 => XTerm.Input.Key.F3, 
			global::Avalonia.Input.Key.F4 => XTerm.Input.Key.F4, 
			global::Avalonia.Input.Key.F5 => XTerm.Input.Key.F5, 
			global::Avalonia.Input.Key.F6 => XTerm.Input.Key.F6, 
			global::Avalonia.Input.Key.F7 => XTerm.Input.Key.F7, 
			global::Avalonia.Input.Key.F8 => XTerm.Input.Key.F8, 
			global::Avalonia.Input.Key.F9 => XTerm.Input.Key.F9, 
			global::Avalonia.Input.Key.F10 => XTerm.Input.Key.F10, 
			global::Avalonia.Input.Key.F11 => XTerm.Input.Key.F11, 
			global::Avalonia.Input.Key.F12 => XTerm.Input.Key.F12, 
			_ => null, 
		};
	}

	private XTerm.Input.KeyModifiers ConvertAvaloniaModifiers(global::Avalonia.Input.KeyModifiers modifiers)
	{
		XTerm.Input.KeyModifiers keyModifiers = XTerm.Input.KeyModifiers.None;
		if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift))
		{
			keyModifiers |= XTerm.Input.KeyModifiers.Shift;
		}
		if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control))
		{
			keyModifiers |= XTerm.Input.KeyModifiers.Control;
		}
		if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt))
		{
			keyModifiers |= XTerm.Input.KeyModifiers.Alt;
		}
		return keyModifiers;
	}

	private static string KittyKeyName(KeyEventArgs e)
	{
		switch (e.Key)
		{
		case global::Avalonia.Input.Key.Up:
			return "ArrowUp";
		case global::Avalonia.Input.Key.Down:
			return "ArrowDown";
		case global::Avalonia.Input.Key.Left:
			return "ArrowLeft";
		case global::Avalonia.Input.Key.Right:
			return "ArrowRight";
		case global::Avalonia.Input.Key.Home:
			return "Home";
		case global::Avalonia.Input.Key.End:
			return "End";
		case global::Avalonia.Input.Key.PageUp:
			return "PageUp";
		case global::Avalonia.Input.Key.PageDown:
			return "PageDown";
		case global::Avalonia.Input.Key.Insert:
			return "Insert";
		case global::Avalonia.Input.Key.Delete:
			return "Delete";
		case global::Avalonia.Input.Key.Escape:
			return "Escape";
		case global::Avalonia.Input.Key.Return:
			return "Enter";
		case global::Avalonia.Input.Key.Tab:
			return "Tab";
		case global::Avalonia.Input.Key.Back:
			return "Backspace";
		case global::Avalonia.Input.Key.CapsLock:
			return "CapsLock";
		case global::Avalonia.Input.Key.Scroll:
			return "ScrollLock";
		case global::Avalonia.Input.Key.NumLock:
			return "NumLock";
		case global::Avalonia.Input.Key.Snapshot:
			return "PrintScreen";
		case global::Avalonia.Input.Key.Pause:
			return "Pause";
		case global::Avalonia.Input.Key.Apps:
			return "ContextMenu";
		case global::Avalonia.Input.Key.LeftShift:
		case global::Avalonia.Input.Key.RightShift:
			return "Shift";
		case global::Avalonia.Input.Key.LeftCtrl:
		case global::Avalonia.Input.Key.RightCtrl:
			return "Control";
		case global::Avalonia.Input.Key.LeftAlt:
		case global::Avalonia.Input.Key.RightAlt:
			return "Alt";
		case global::Avalonia.Input.Key.LWin:
		case global::Avalonia.Input.Key.RWin:
			return "Meta";
		case global::Avalonia.Input.Key.F1:
		case global::Avalonia.Input.Key.F2:
		case global::Avalonia.Input.Key.F3:
		case global::Avalonia.Input.Key.F4:
		case global::Avalonia.Input.Key.F5:
		case global::Avalonia.Input.Key.F6:
		case global::Avalonia.Input.Key.F7:
		case global::Avalonia.Input.Key.F8:
		case global::Avalonia.Input.Key.F9:
		case global::Avalonia.Input.Key.F10:
		case global::Avalonia.Input.Key.F11:
		case global::Avalonia.Input.Key.F12:
		case global::Avalonia.Input.Key.F13:
		case global::Avalonia.Input.Key.F14:
		case global::Avalonia.Input.Key.F15:
		case global::Avalonia.Input.Key.F16:
		case global::Avalonia.Input.Key.F17:
		case global::Avalonia.Input.Key.F18:
		case global::Avalonia.Input.Key.F19:
		case global::Avalonia.Input.Key.F20:
		case global::Avalonia.Input.Key.F21:
		case global::Avalonia.Input.Key.F22:
		case global::Avalonia.Input.Key.F23:
		case global::Avalonia.Input.Key.F24:
			return "F" + ((int)(e.Key - 90 + 1)).ToString(CultureInfo.InvariantCulture);
		default:
		{
			string keySymbol = e.KeySymbol;
			return (keySymbol != null && keySymbol.Length == 1) ? keySymbol : string.Empty;
		}
		}
	}

	private static string KittyPhysicalCode(KeyEventArgs e)
	{
		PhysicalKey physicalKey = e.PhysicalKey;
		if (physicalKey == PhysicalKey.None)
		{
			return string.Empty;
		}
		string text = physicalKey.ToString();
		if (text.Length == 1 && text[0] >= 'A' && text[0] <= 'Z')
		{
			return "Key" + text;
		}
		if (text.StartsWith("NumPad", StringComparison.Ordinal))
		{
			return "Numpad" + text.Substring("NumPad".Length);
		}
		return text;
	}

	private static bool TryBuildKittyKeyEvent(KeyEventArgs e, out KeyEvent ev)
	{
		string text = KittyKeyName(e);
		if (text.Length == 0)
		{
			ev = null;
			return false;
		}
		ev = new KeyEvent
		{
			Key = text,
			Code = KittyPhysicalCode(e),
			CtrlKey = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control),
			AltKey = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt),
			ShiftKey = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift),
			MetaKey = e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Meta)
		};
		return true;
	}

	private async Task<bool> TrySendKittyKeyAsync(KeyEventArgs e, KittyKeyboardEventType eventType, string pendingErase = "")
	{
		if (!_terminal.KittyKeyboardActive)
		{
			_keysHeld.Clear();
			return false;
		}
		if (!TryBuildKittyKeyEvent(e, out KeyEvent ev))
		{
			return false;
		}
		if (eventType == KittyKeyboardEventType.Press && !_keysHeld.Add((e.PhysicalKey, e.Key)))
		{
			eventType = KittyKeyboardEventType.Repeat;
		}
		else if (eventType == KittyKeyboardEventType.Release && !_keysHeld.Remove((e.PhysicalKey, e.Key)))
		{
			e.Handled = true;
			return true;
		}
		string text = _terminal.GenerateKittyKeyInput(ev, eventType);
		e.Handled = true;
		bool flag = pendingErase.Length > 0;
		if (flag)
		{
			global::Avalonia.Input.Key key = e.Key;
			bool flag2 = ((key == global::Avalonia.Input.Key.Back || key == global::Avalonia.Input.Key.Delete) ? true : false);
			flag = flag2;
		}
		text = ((!flag) ? (pendingErase + text) : pendingErase);
		if (!string.IsNullOrEmpty(text))
		{
			await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
		}
		return true;
	}

	private static bool IsClipboardTarget(string target)
	{
		if (!target.Contains('c'))
		{
			return target.Contains('s');
		}
		return true;
	}

	private void OnTerminalClipboardWriteRequested(object? sender, TerminalEvents.ClipboardWriteEventArgs e)
	{
		if (!IsClipboardTarget(e.Target))
		{
			return;
		}
		int num = -1;
		for (int i = 0; i < e.Formats.Count; i++)
		{
			if (e.Formats[i].MimeType == "text/plain")
			{
				num = i;
				break;
			}
			if (num < 0 && e.Formats[i].MimeType.StartsWith("text/", StringComparison.Ordinal))
			{
				num = i;
			}
		}
		if (num >= 0)
		{
			string text = Encoding.UTF8.GetString(e.Formats[num].Data);
			global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				QueueClipboardWriteAsync(text);
			});
		}
	}

	private Task QueueClipboardWriteAsync(string text)
	{
		_clipboardWrites = _clipboardWrites.ContinueWith((Task _) => WriteToClipboardAsync(text), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
		return _clipboardWrites;
	}

	private async Task WriteToClipboardAsync(string text)
	{
		IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
		if (clipboard == null)
		{
			return;
		}
		try
		{
			if (text.Length == 0)
			{
				await clipboard.ClearAsync();
			}
			else
			{
				await clipboard.SetTextAsync(text);
			}
		}
		catch
		{
		}
	}

	private void OnTerminalClipboardReadRequested(object? sender, TerminalEvents.ClipboardReadEventArgs e)
	{
		if (!IsClipboardTarget(e.Target))
		{
			return;
		}
		if (e.MimeType == ".")
		{
			e.Text = "text/plain";
		}
		else if (e.MimeType.Length == 0 || !(e.MimeType != "text/plain"))
		{
			e.Defer();
			global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => RespondFromClipboardAsync(e));
		}
	}

	private async Task RespondFromClipboardAsync(TerminalEvents.ClipboardReadEventArgs e)
	{
		string text2;
		try
		{
			IClipboard clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
			string text = ((clipboard != null) ? (await clipboard.TryGetTextAsync()) : null);
			text2 = text;
		}
		catch
		{
			text2 = null;
		}
		lock (_terminalLock)
		{
			e.Respond(text2);
		}
	}

	private bool TryGetPrintableChar(KeyEventArgs e, out char character)
	{
		if (!string.IsNullOrEmpty(e.KeySymbol) && e.KeySymbol.Length == 1 && !char.IsControl(e.KeySymbol[0]))
		{
			character = e.KeySymbol[0];
			return true;
		}
		return TryMapKeyToChar(e.Key, e.KeyModifiers, out character);
	}

	private bool TryMapKeyToChar(global::Avalonia.Input.Key key, global::Avalonia.Input.KeyModifiers modifiers, out char character)
	{
		character = '\0';
		bool flag = modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift);
		if (key >= global::Avalonia.Input.Key.A && key <= global::Avalonia.Input.Key.Z)
		{
			int num = (int)(key - 44);
			character = (char)((flag ? 65 : 97) + num);
			return true;
		}
		if (key >= global::Avalonia.Input.Key.D0 && key <= global::Avalonia.Input.Key.D9)
		{
			if (flag)
			{
				character = key switch
				{
					global::Avalonia.Input.Key.D1 => '!', 
					global::Avalonia.Input.Key.D2 => '@', 
					global::Avalonia.Input.Key.D3 => '#', 
					global::Avalonia.Input.Key.D4 => '$', 
					global::Avalonia.Input.Key.D5 => '%', 
					global::Avalonia.Input.Key.D6 => '^', 
					global::Avalonia.Input.Key.D7 => '&', 
					global::Avalonia.Input.Key.D8 => '*', 
					global::Avalonia.Input.Key.D9 => '(', 
					global::Avalonia.Input.Key.D0 => ')', 
					_ => '\0', 
				};
			}
			else
			{
				int num2 = (int)(key - 34);
				character = (char)(48 + num2);
			}
			return character != '\0';
		}
		if (key >= global::Avalonia.Input.Key.NumPad0 && key <= global::Avalonia.Input.Key.NumPad9)
		{
			int num3 = (int)(key - 74);
			character = (char)(48 + num3);
			return true;
		}
		character = key switch
		{
			global::Avalonia.Input.Key.Space => ' ', 
			global::Avalonia.Input.Key.OemPeriod => flag ? '>' : '.', 
			global::Avalonia.Input.Key.OemComma => flag ? '<' : ',', 
			global::Avalonia.Input.Key.OemMinus => flag ? '_' : '-', 
			global::Avalonia.Input.Key.OemPlus => flag ? '+' : '=', 
			global::Avalonia.Input.Key.OemSemicolon => flag ? ':' : ';', 
			global::Avalonia.Input.Key.OemQuotes => flag ? '"' : '\'', 
			global::Avalonia.Input.Key.OemTilde => flag ? '~' : '`', 
			global::Avalonia.Input.Key.OemOpenBrackets => flag ? '{' : '[', 
			global::Avalonia.Input.Key.OemCloseBrackets => flag ? '}' : ']', 
			global::Avalonia.Input.Key.OemPipe => flag ? '|' : '\\', 
			global::Avalonia.Input.Key.OemBackslash => flag ? '|' : '\\', 
			global::Avalonia.Input.Key.OemQuestion => flag ? '?' : '/', 
			global::Avalonia.Input.Key.Multiply => '*', 
			global::Avalonia.Input.Key.Add => '+', 
			global::Avalonia.Input.Key.Subtract => '-', 
			global::Avalonia.Input.Key.Divide => '/', 
			global::Avalonia.Input.Key.Decimal => '.', 
			_ => '\0', 
		};
		return character != '\0';
	}

	internal static void CopyPixels(TerminalImage image, nint destination, int destinationRowBytes)
	{
		if (!MemoryMarshal.TryGetArray(image.CurrentPixels, out var segment))
		{
			segment = new ArraySegment<byte>(image.CurrentPixels.ToArray());
		}
		int stride = image.Stride;
		if (destinationRowBytes == stride)
		{
			Marshal.Copy(segment.Array, segment.Offset, destination, stride * image.PixelHeight);
			return;
		}
		for (int i = 0; i < image.PixelHeight; i++)
		{
			Marshal.Copy(segment.Array, segment.Offset + i * stride, IntPtr.Add(destination, i * destinationRowBytes), stride);
		}
	}

	private string GenerateWin32InputSequence(KeyEventArgs e, bool isKeyDown)
	{
		int num = ConvertAvaloniaKeyToVirtualKey(e.Key);
		if (num == 0)
		{
			return string.Empty;
		}
		int scanCode = 0;
		int num2 = 0;
		char character;
		if (!string.IsNullOrEmpty(e.KeySymbol) && e.KeySymbol.Length >= 1)
		{
			num2 = char.ConvertToUtf32(e.KeySymbol, 0);
		}
		else if (TryMapKeyToChar(e.Key, e.KeyModifiers, out character))
		{
			num2 = character;
		}
		else if (e.Key == global::Avalonia.Input.Key.Return)
		{
			num2 = 13;
		}
		else if (e.Key == global::Avalonia.Input.Key.Tab)
		{
			num2 = 9;
		}
		else if (e.Key == global::Avalonia.Input.Key.Back)
		{
			num2 = 8;
		}
		else if (e.Key == global::Avalonia.Input.Key.Escape)
		{
			num2 = 27;
		}
		else if (e.Key == global::Avalonia.Input.Key.Space)
		{
			num2 = 32;
		}
		if ((e.KeyModifiers & global::Avalonia.Input.KeyModifiers.Control) != 0 && num2 != 0 && !IsAltGrComposed(e, out var composed))
		{
			if (num2 >= 97 && num2 <= 122)
			{
				num2 = num2 - 97 + 1;
			}
			else
			{
				num2 = ((num2 < 65 || num2 > 90) ? (num2 switch
				{
					32 => 0, 
					64 => 0, 
					91 => 27, 
					92 => 28, 
					93 => 29, 
					94 => 30, 
					95 => 31, 
					63 => 127, 
					_ => num2, 
				}) : (num2 - 65 + 1));
			}
		}
		Win32ControlKeyState win32ControlKeyState = GetWin32ControlKeyState(e.KeyModifiers, e.Key);
		if (IsAltGrComposed(e, out composed))
		{
			win32ControlKeyState &= ~Win32ControlKeyState.LeftAltPressed;
			win32ControlKeyState |= Win32ControlKeyState.RightAltPressed;
		}
		return Win32Record(num, scanCode, num2, isKeyDown, win32ControlKeyState);
	}

	private static string Win32Record(int vk, int scanCode, int unicodeChar, bool isKeyDown, Win32ControlKeyState controlKeyState)
	{
		int value = 1;
		return $"\u001b[{vk};{scanCode};{unicodeChar};{(isKeyDown ? 1 : 0)};{(int)controlKeyState};{value}_";
	}

	private static string Win32TextRecords(string text)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (char unicodeChar in text)
		{
			stringBuilder.Append(Win32Record(231, 0, unicodeChar, isKeyDown: true, Win32ControlKeyState.None));
			stringBuilder.Append(Win32Record(231, 0, unicodeChar, isKeyDown: false, Win32ControlKeyState.None));
		}
		return stringBuilder.ToString();
	}

	private static Win32ControlKeyState GetWin32ControlKeyState(global::Avalonia.Input.KeyModifiers modifiers, global::Avalonia.Input.Key key)
	{
		Win32ControlKeyState win32ControlKeyState = Win32ControlKeyState.None;
		if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift))
		{
			win32ControlKeyState |= Win32ControlKeyState.ShiftPressed;
		}
		if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control))
		{
			win32ControlKeyState |= Win32ControlKeyState.LeftCtrlPressed;
		}
		if (modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Alt))
		{
			win32ControlKeyState |= Win32ControlKeyState.LeftAltPressed;
		}
		if (key == global::Avalonia.Input.Key.RightAlt)
		{
			win32ControlKeyState &= ~Win32ControlKeyState.LeftAltPressed;
			win32ControlKeyState |= Win32ControlKeyState.RightAltPressed;
		}
		if (key == global::Avalonia.Input.Key.RightCtrl)
		{
			win32ControlKeyState &= ~Win32ControlKeyState.LeftCtrlPressed;
			win32ControlKeyState |= Win32ControlKeyState.RightCtrlPressed;
		}
		foreach (var (flag, win32ControlKeyState2) in LockStates())
		{
			if (flag)
			{
				win32ControlKeyState |= win32ControlKeyState2;
			}
		}
		if (IsEnhancedKey(key))
		{
			win32ControlKeyState |= Win32ControlKeyState.EnhancedKey;
		}
		return win32ControlKeyState;
	}

	private static IEnumerable<(bool On, Win32ControlKeyState Flag)> LockStates()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			yield return (On: IsToggled(20), Flag: Win32ControlKeyState.CapsLockOn);
			yield return (On: IsToggled(144), Flag: Win32ControlKeyState.NumLockOn);
			yield return (On: IsToggled(145), Flag: Win32ControlKeyState.ScrollLockOn);
		}
	}

	private static bool IsToggled(int virtualKey)
	{
		try
		{
			return (GetKeyState(virtualKey) & 1) != 0;
		}
		catch (EntryPointNotFoundException)
		{
			return false;
		}
		catch (DllNotFoundException)
		{
			return false;
		}
	}

	[DllImport("user32.dll")]
	private static extern short GetKeyState(int nVirtKey);

	private bool IsAltGrComposed(KeyEventArgs e, out char composed)
	{
		composed = '\0';
		if ((e.KeyModifiers & (global::Avalonia.Input.KeyModifiers.Alt | global::Avalonia.Input.KeyModifiers.Control)) != (global::Avalonia.Input.KeyModifiers.Alt | global::Avalonia.Input.KeyModifiers.Control))
		{
			return false;
		}
		if ((e.KeyModifiers & global::Avalonia.Input.KeyModifiers.Meta) != 0)
		{
			return false;
		}
		string keySymbol = e.KeySymbol;
		if (keySymbol == null || keySymbol.Length != 1)
		{
			return false;
		}
		char c = keySymbol[0];
		if (char.IsControl(c))
		{
			return false;
		}
		if (TryMapKeyToChar(e.Key, global::Avalonia.Input.KeyModifiers.None, out var character) && char.ToLowerInvariant(character) == char.ToLowerInvariant(c))
		{
			return false;
		}
		composed = c;
		return true;
	}

	private static bool IsEnhancedKey(global::Avalonia.Input.Key key)
	{
		switch (key)
		{
		case global::Avalonia.Input.Key.Pause:
		case global::Avalonia.Input.Key.PageUp:
		case global::Avalonia.Input.Key.PageDown:
		case global::Avalonia.Input.Key.End:
		case global::Avalonia.Input.Key.Home:
		case global::Avalonia.Input.Key.Left:
		case global::Avalonia.Input.Key.Up:
		case global::Avalonia.Input.Key.Right:
		case global::Avalonia.Input.Key.Down:
		case global::Avalonia.Input.Key.Snapshot:
		case global::Avalonia.Input.Key.Insert:
		case global::Avalonia.Input.Key.Delete:
		case global::Avalonia.Input.Key.Divide:
		case global::Avalonia.Input.Key.NumLock:
		case global::Avalonia.Input.Key.RightCtrl:
		case global::Avalonia.Input.Key.RightAlt:
			return true;
		default:
			return false;
		}
	}

	private static int ConvertAvaloniaKeyToVirtualKey(global::Avalonia.Input.Key key)
	{
		return key switch
		{
			global::Avalonia.Input.Key.A => 65, 
			global::Avalonia.Input.Key.B => 66, 
			global::Avalonia.Input.Key.C => 67, 
			global::Avalonia.Input.Key.D => 68, 
			global::Avalonia.Input.Key.E => 69, 
			global::Avalonia.Input.Key.F => 70, 
			global::Avalonia.Input.Key.G => 71, 
			global::Avalonia.Input.Key.H => 72, 
			global::Avalonia.Input.Key.I => 73, 
			global::Avalonia.Input.Key.J => 74, 
			global::Avalonia.Input.Key.K => 75, 
			global::Avalonia.Input.Key.L => 76, 
			global::Avalonia.Input.Key.M => 77, 
			global::Avalonia.Input.Key.N => 78, 
			global::Avalonia.Input.Key.O => 79, 
			global::Avalonia.Input.Key.P => 80, 
			global::Avalonia.Input.Key.Q => 81, 
			global::Avalonia.Input.Key.R => 82, 
			global::Avalonia.Input.Key.S => 83, 
			global::Avalonia.Input.Key.T => 84, 
			global::Avalonia.Input.Key.U => 85, 
			global::Avalonia.Input.Key.V => 86, 
			global::Avalonia.Input.Key.W => 87, 
			global::Avalonia.Input.Key.X => 88, 
			global::Avalonia.Input.Key.Y => 89, 
			global::Avalonia.Input.Key.Z => 90, 
			global::Avalonia.Input.Key.D0 => 48, 
			global::Avalonia.Input.Key.D1 => 49, 
			global::Avalonia.Input.Key.D2 => 50, 
			global::Avalonia.Input.Key.D3 => 51, 
			global::Avalonia.Input.Key.D4 => 52, 
			global::Avalonia.Input.Key.D5 => 53, 
			global::Avalonia.Input.Key.D6 => 54, 
			global::Avalonia.Input.Key.D7 => 55, 
			global::Avalonia.Input.Key.D8 => 56, 
			global::Avalonia.Input.Key.D9 => 57, 
			global::Avalonia.Input.Key.F1 => 112, 
			global::Avalonia.Input.Key.F2 => 113, 
			global::Avalonia.Input.Key.F3 => 114, 
			global::Avalonia.Input.Key.F4 => 115, 
			global::Avalonia.Input.Key.F5 => 116, 
			global::Avalonia.Input.Key.F6 => 117, 
			global::Avalonia.Input.Key.F7 => 118, 
			global::Avalonia.Input.Key.F8 => 119, 
			global::Avalonia.Input.Key.F9 => 120, 
			global::Avalonia.Input.Key.F10 => 121, 
			global::Avalonia.Input.Key.F11 => 122, 
			global::Avalonia.Input.Key.F12 => 123, 
			global::Avalonia.Input.Key.F13 => 124, 
			global::Avalonia.Input.Key.F14 => 125, 
			global::Avalonia.Input.Key.F15 => 126, 
			global::Avalonia.Input.Key.F16 => 127, 
			global::Avalonia.Input.Key.F17 => 128, 
			global::Avalonia.Input.Key.F18 => 129, 
			global::Avalonia.Input.Key.F19 => 130, 
			global::Avalonia.Input.Key.F20 => 131, 
			global::Avalonia.Input.Key.F21 => 132, 
			global::Avalonia.Input.Key.F22 => 133, 
			global::Avalonia.Input.Key.F23 => 134, 
			global::Avalonia.Input.Key.F24 => 135, 
			global::Avalonia.Input.Key.Left => 37, 
			global::Avalonia.Input.Key.Up => 38, 
			global::Avalonia.Input.Key.Right => 39, 
			global::Avalonia.Input.Key.Down => 40, 
			global::Avalonia.Input.Key.Home => 36, 
			global::Avalonia.Input.Key.End => 35, 
			global::Avalonia.Input.Key.PageUp => 33, 
			global::Avalonia.Input.Key.PageDown => 34, 
			global::Avalonia.Input.Key.Insert => 45, 
			global::Avalonia.Input.Key.Delete => 46, 
			global::Avalonia.Input.Key.Back => 8, 
			global::Avalonia.Input.Key.Tab => 9, 
			global::Avalonia.Input.Key.Return => 13, 
			global::Avalonia.Input.Key.Escape => 27, 
			global::Avalonia.Input.Key.Space => 32, 
			global::Avalonia.Input.Key.Pause => 19, 
			global::Avalonia.Input.Key.CapsLock => 20, 
			global::Avalonia.Input.Key.NumLock => 144, 
			global::Avalonia.Input.Key.Scroll => 145, 
			global::Avalonia.Input.Key.Snapshot => 44, 
			global::Avalonia.Input.Key.LeftShift => 16, 
			global::Avalonia.Input.Key.RightShift => 16, 
			global::Avalonia.Input.Key.LeftCtrl => 17, 
			global::Avalonia.Input.Key.RightCtrl => 17, 
			global::Avalonia.Input.Key.LeftAlt => 18, 
			global::Avalonia.Input.Key.RightAlt => 18, 
			global::Avalonia.Input.Key.LWin => 91, 
			global::Avalonia.Input.Key.RWin => 92, 
			global::Avalonia.Input.Key.NumPad0 => 96, 
			global::Avalonia.Input.Key.NumPad1 => 97, 
			global::Avalonia.Input.Key.NumPad2 => 98, 
			global::Avalonia.Input.Key.NumPad3 => 99, 
			global::Avalonia.Input.Key.NumPad4 => 100, 
			global::Avalonia.Input.Key.NumPad5 => 101, 
			global::Avalonia.Input.Key.NumPad6 => 102, 
			global::Avalonia.Input.Key.NumPad7 => 103, 
			global::Avalonia.Input.Key.NumPad8 => 104, 
			global::Avalonia.Input.Key.NumPad9 => 105, 
			global::Avalonia.Input.Key.Multiply => 106, 
			global::Avalonia.Input.Key.Add => 107, 
			global::Avalonia.Input.Key.Separator => 108, 
			global::Avalonia.Input.Key.Subtract => 109, 
			global::Avalonia.Input.Key.Decimal => 110, 
			global::Avalonia.Input.Key.Divide => 111, 
			global::Avalonia.Input.Key.OemSemicolon => 186, 
			global::Avalonia.Input.Key.OemPlus => 187, 
			global::Avalonia.Input.Key.OemComma => 188, 
			global::Avalonia.Input.Key.OemMinus => 189, 
			global::Avalonia.Input.Key.OemPeriod => 190, 
			global::Avalonia.Input.Key.OemQuestion => 191, 
			global::Avalonia.Input.Key.OemTilde => 192, 
			global::Avalonia.Input.Key.OemOpenBrackets => 219, 
			global::Avalonia.Input.Key.OemPipe => 220, 
			global::Avalonia.Input.Key.OemCloseBrackets => 221, 
			global::Avalonia.Input.Key.OemQuotes => 222, 
			global::Avalonia.Input.Key.OemBackslash => 226, 
			_ => 0, 
		};
	}

	protected override async void OnPointerPressed(PointerPressedEventArgs e)
	{
		base.OnPointerPressed(e);
		Focus();
		try
		{
			Point position = e.GetPosition(this);
			int num = PointerColumn(position.X);
			int num2 = PointerRow(position.Y);
			_kbSelAnchor = null;
			_kbSelWholeInput = false;
			_pendingUrlClick = null;
			if (e.KeyModifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Control) && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				HoveredUrl hoveredUrl = FindUrlAtColumn(_terminal.Buffer.ViewportY + num2, num);
				if (hoveredUrl != null)
				{
					_pendingUrlClick = hoveredUrl;
					e.Handled = true;
					return;
				}
			}
			if (ShouldHandleSelection(e.KeyModifiers))
			{
				PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
				if (properties.IsRightButtonPressed)
				{
					e.Handled = true;
					if (_terminal.Selection.HasSelection)
					{
						await CopyAsync();
						_terminal.Selection.ClearSelection();
						RequestPaint();
					}
					else
					{
						await PasteAsync();
					}
					return;
				}
				if (properties.IsLeftButtonPressed && _terminal.Selection.HasSelection)
				{
					_terminal.Selection.ClearSelection();
					RequestPaint();
				}
				XTerm.Selection.SelectionMode selectionMode = e.ClickCount switch
				{
					2 => XTerm.Selection.SelectionMode.Word, 
					3 => XTerm.Selection.SelectionMode.Line, 
					_ => XTerm.Selection.SelectionMode.Normal, 
				};
				if (selectionMode == XTerm.Selection.SelectionMode.Normal && !ShowCaretOnClick)
				{
					_pendingSelectionStart = (num, _terminal.Buffer.ViewportY + num2);
					_isSelecting = true;
				}
				else
				{
					int num3 = num2;
					_terminal.Selection.StartSelection(num, num3, selectionMode);
					if (selectionMode == XTerm.Selection.SelectionMode.Word)
					{
						ExtendSelectionToPath(num, num3);
					}
					_isSelecting = true;
					_pendingSelectionStart = null;
					RequestPaint();
				}
				e.Handled = true;
			}
			else if (_ptyConnection != null)
			{
				XTerm.Input.MouseButton button = ConvertPointerButton(e.GetCurrentPoint(this).Properties);
				XTerm.Input.KeyModifiers modifiers = ConvertAvaloniaModifiers(e.KeyModifiers);
				string text = _terminal.GenerateMouseEvent(button, num, num2, PointerPixelX(position.X), PointerPixelY(position.Y), MouseEventType.Down, modifiers);
				if (!string.IsNullOrEmpty(text))
				{
					await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void ExtendSelectionToPath(int col, int viewportRow)
	{
		BufferLine line = _terminal.Buffer.GetLine(_terminal.Buffer.ViewportY + viewportRow);
		if (line == null || col < 0 || col >= line.Length)
		{
			return;
		}
		int num = col;
		int num2 = col;
		int num3 = col - 1;
		while (num3 >= 0 && IsPathCell(line, num3))
		{
			num = num3;
			num3--;
		}
		for (int i = col + 1; i < line.Length && IsPathCell(line, i); i++)
		{
			num2 = i;
		}
		bool flag = false;
		for (int j = num; j <= num2; j++)
		{
			if (flag)
			{
				break;
			}
			string content = line[j].Content;
			bool flag2 = content != null && content.Length > 0;
			if (flag2)
			{
				char c = content[0];
				bool flag3 = ((c == '.' || c == '/' || c == '~') ? true : false);
				flag2 = flag3;
			}
			if (flag2)
			{
				flag = true;
			}
		}
		if (flag)
		{
			_terminal.Selection.StartSelection(num, viewportRow);
			_terminal.Selection.UpdateSelection(num2, viewportRow);
			RequestPaint();
		}
	}

	private static bool IsPathCell(BufferLine line, int col)
	{
		string content = line[col].Content;
		bool flag = content != null && content.Length > 0;
		if (flag)
		{
			bool flag2 = char.IsLetterOrDigit(content[0]);
			if (!flag2)
			{
				bool flag3;
				switch (content[0])
				{
				case '-':
				case '.':
				case '/':
				case '_':
				case '~':
					flag3 = true;
					break;
				default:
					flag3 = false;
					break;
				}
				flag2 = flag3;
			}
			flag = flag2;
		}
		return flag;
	}

	protected override async void OnPointerReleased(PointerReleasedEventArgs e)
	{
		base.OnPointerReleased(e);
		try
		{
			HoveredUrl pendingUrlClick = _pendingUrlClick;
			_pendingUrlClick = null;
			if (pendingUrlClick != null)
			{
				Point position = e.GetPosition(this);
				int col = PointerColumn(position.X);
				int num = PointerRow(position.Y);
				HoveredUrl hoveredUrl = FindUrlAtColumn(_terminal.Buffer.ViewportY + num, col);
				if (hoveredUrl != null && hoveredUrl.Url == pendingUrlClick.Url)
				{
					UrlClicked?.Invoke(this, new UrlClickedEventArgs(pendingUrlClick.Url, pendingUrlClick.FromSequence));
				}
				e.Handled = true;
			}
			else if (_isSelecting)
			{
				if (_pendingSelectionStart.HasValue)
				{
					_pendingSelectionStart = null;
				}
				else
				{
					_terminal.Selection.EndSelection();
					if (AutoCopyOnSelection && _terminal.Selection.HasSelection)
					{
						CopyOnSelectionAsync();
					}
				}
				_isSelecting = false;
				e.Handled = true;
			}
			else if (_ptyConnection != null)
			{
				Point position2 = e.GetPosition(this);
				int x = PointerColumn(position2.X);
				int y = PointerRow(position2.Y);
				XTerm.Input.MouseButton button = ConvertPointerButton(e.GetCurrentPoint(this).Properties, e.InitialPressMouseButton);
				XTerm.Input.KeyModifiers modifiers = ConvertAvaloniaModifiers(e.KeyModifiers);
				string text = _terminal.GenerateMouseEvent(button, x, y, PointerPixelX(position2.X), PointerPixelY(position2.Y), MouseEventType.Up, modifiers);
				if (!string.IsNullOrEmpty(text))
				{
					await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		catch (Exception)
		{
		}
	}

	protected override async void OnPointerMoved(PointerEventArgs e)
	{
		base.OnPointerMoved(e);
		try
		{
			Point position = e.GetPosition(this);
			int num = PointerColumn(position.X);
			int num2 = PointerRow(position.Y);
			if (_isSelecting)
			{
				ClearHoveredUrl();
				int y = num2;
				if (_pendingSelectionStart.HasValue)
				{
					int y2 = _pendingSelectionStart.Value.Row - _terminal.Buffer.ViewportY;
					_terminal.Selection.StartSelection(_pendingSelectionStart.Value.Col, y2);
					_pendingSelectionStart = null;
				}
				_terminal.Selection.UpdateSelection(num, y);
				ScrollForDragAt(position.Y);
				RequestPaint();
				e.Handled = true;
				return;
			}
			int bufferLine = _terminal.Buffer.ViewportY + num2;
			UpdateHoveredUrl(bufferLine, num);
			ApplyTextCursor();
			if (_ptyConnection == null)
			{
				return;
			}
			PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
			XTerm.Input.KeyModifiers keyModifiers = ConvertAvaloniaModifiers(e.KeyModifiers);
			XTerm.Input.MouseButton mouseButton = ConvertPointerButton(properties);
			MouseEventType mouseEventType = ((properties.IsLeftButtonPressed || properties.IsMiddleButtonPressed || properties.IsRightButtonPressed) ? MouseEventType.Drag : MouseEventType.Move);
			int num3 = PointerPixelX(position.X);
			int num4 = PointerPixelY(position.Y);
			MouseEncoding mouseEncoding = _terminal.MouseEncoding;
			bool flag = mouseEncoding == MouseEncoding.SGRPixels;
			(int, int, MouseEncoding, MouseEventType, MouseTrackingMode, XTerm.Input.MouseButton, XTerm.Input.KeyModifiers) tuple = (flag ? num3 : num, flag ? num4 : num2, mouseEncoding, mouseEventType, _terminal.MouseTrackingMode, mouseButton, keyModifiers);
			(int, int, MouseEncoding, MouseEventType, MouseTrackingMode, XTerm.Input.MouseButton, XTerm.Input.KeyModifiers)? lastReportedMotion = _lastReportedMotion;
			(int, int, MouseEncoding, MouseEventType, MouseTrackingMode, XTerm.Input.MouseButton, XTerm.Input.KeyModifiers) tuple2 = tuple;
			bool hasValue = lastReportedMotion.HasValue;
			int num5;
			if (!hasValue)
			{
				num5 = 0;
			}
			else if (!hasValue)
			{
				num5 = 1;
			}
			else
			{
				(int, int, MouseEncoding, MouseEventType, MouseTrackingMode, XTerm.Input.MouseButton, XTerm.Input.KeyModifiers) valueOrDefault = lastReportedMotion.GetValueOrDefault();
				num5 = ((valueOrDefault.Item1 == tuple2.Item1 && valueOrDefault.Item2 == tuple2.Item2 && valueOrDefault.Item3 == tuple2.Item3 && valueOrDefault.Item4 == tuple2.Item4 && valueOrDefault.Item5 == tuple2.Item5 && valueOrDefault.Item6 == tuple2.Item6 && valueOrDefault.Item7 == tuple2.Item7) ? 1 : 0);
			}
			if (num5 == 0)
			{
				string text = _terminal.GenerateMouseEvent(mouseButton, num, num2, num3, num4, mouseEventType, keyModifiers);
				if (!string.IsNullOrEmpty(text))
				{
					_lastReportedMotion = tuple;
					await SendToPtyAsync(text).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					_lastReportedMotion = null;
				}
			}
		}
		catch (Exception)
		{
		}
	}

	private void ScrollForDragAt(double y)
	{
		if (_charHeight <= 0.0)
		{
			return;
		}
		int num = ((y < 0.0) ? (-1) : ((y > (double)_terminal.Rows * _charHeight) ? 1 : 0));
		if (num != 0)
		{
			int num2 = Math.Clamp(ViewportY + num, 0, MaxScrollback);
			if (num2 != ViewportY)
			{
				ViewportY = num2;
			}
		}
	}

	protected override async void OnPointerWheelChanged(PointerWheelEventArgs e)
	{
		base.OnPointerWheelChanged(e);
		double y = e.Delta.Y;
		if (y == 0.0)
		{
			return;
		}
		if (_ptyConnection != null && !ShouldHandleSelection(e.KeyModifiers))
		{
			int num = TakeWheelSteps(ref _wheelResidualApp, y);
			if (num == 0)
			{
				e.Handled = true;
				return;
			}
			Point position = e.GetPosition(this);
			int x = PointerColumn(position.X);
			int y2 = PointerRow(position.Y);
			XTerm.Input.KeyModifiers modifiers = ConvertAvaloniaModifiers(e.KeyModifiers);
			int pixelX = PointerPixelX(position.X);
			int pixelY = PointerPixelY(position.Y);
			XTerm.Input.MouseButton button = ((num > 0) ? XTerm.Input.MouseButton.WheelUp : XTerm.Input.MouseButton.WheelDown);
			MouseEventType eventType = ((num > 0) ? MouseEventType.WheelUp : MouseEventType.WheelDown);
			string text = _terminal.GenerateMouseEvent(button, x, y2, pixelX, pixelY, eventType, modifiers);
			if (!string.IsNullOrEmpty(text))
			{
				e.Handled = true;
				string data = string.Concat(Enumerable.Repeat(text, Math.Min(Math.Abs(num), 12)));
				await SendToPtyAsync(data).ConfigureAwait(continueOnCapturedContext: false);
				return;
			}
		}
		int num2 = -TakeWheelSteps(ref _wheelResidual, y * 3.0);
		if (num2 != 0)
		{
			int num3 = Math.Clamp(ViewportY + num2, 0, MaxScrollback);
			if (num3 != ViewportY)
			{
				ViewportY = num3;
			}
		}
		e.Handled = true;
	}

	private static int TakeWheelSteps(ref double residual, double step)
	{
		if (residual != 0.0 && Math.Sign(step) != Math.Sign(residual))
		{
			residual = 0.0;
		}
		residual += step;
		double num = Math.Truncate(residual);
		residual -= num;
		return (int)num;
	}

	private XTerm.Input.MouseButton ConvertPointerButton(PointerPointProperties props, global::Avalonia.Input.MouseButton? releasedButton = null)
	{
		if (props.IsLeftButtonPressed)
		{
			return XTerm.Input.MouseButton.Left;
		}
		if (props.IsMiddleButtonPressed)
		{
			return XTerm.Input.MouseButton.Middle;
		}
		if (props.IsRightButtonPressed)
		{
			return XTerm.Input.MouseButton.Right;
		}
		if (releasedButton.HasValue)
		{
			return releasedButton.Value switch
			{
				global::Avalonia.Input.MouseButton.Left => XTerm.Input.MouseButton.Left, 
				global::Avalonia.Input.MouseButton.Middle => XTerm.Input.MouseButton.Middle, 
				global::Avalonia.Input.MouseButton.Right => XTerm.Input.MouseButton.Right, 
				_ => XTerm.Input.MouseButton.None, 
			};
		}
		return XTerm.Input.MouseButton.None;
	}

	private void UpdateHoveredUrl(int bufferLine, int col)
	{
		(int, int)? lastHoverProbe = _lastHoverProbe;
		if (lastHoverProbe.HasValue)
		{
			(int, int) valueOrDefault = lastHoverProbe.GetValueOrDefault();
			if (valueOrDefault.Item1 == bufferLine && valueOrDefault.Item2 == col)
			{
				return;
			}
		}
		_lastHoverProbe = (bufferLine, col);
		HoveredUrl hoveredUrl = FindUrlAtColumn(bufferLine, col);
		if (hoveredUrl == null)
		{
			ClearHoveredUrl();
		}
		else if (!hoveredUrl.SameAs(_hoveredLink))
		{
			ClearHoveredUrl();
			_hoveredLink = hoveredUrl;
			if (!_cursorOverridden)
			{
				_savedCursor = Cursor;
				_cursorOverridden = true;
			}
			SetCurrentValue(InputElement.CursorProperty, HandCursor);
			RequestPaint();
		}
	}

	private void ClearHoveredUrl()
	{
		_lastHoverProbe = null;
		if (_cursorOverridden)
		{
			SetCurrentValue(InputElement.CursorProperty, _savedCursor);
			_savedCursor = null;
			_cursorOverridden = false;
		}
		if (_hoveredLink != null)
		{
			_hoveredLink = null;
			RequestPaint();
		}
	}

	protected override void OnPointerEntered(PointerEventArgs e)
	{
		base.OnPointerEntered(e);
		ApplyTextCursor();
	}

	private void ApplyTextCursor()
	{
		if (!_cursorOverridden && !_shapeOverridden && Cursor != IBeamCursor)
		{
			SetCurrentValue(InputElement.CursorProperty, IBeamCursor);
		}
	}

	private void OnTerminalPointerShapeChanged(object? sender, TerminalEvents.PointerShapeEventArgs e)
	{
		_pendingPointerShape = e.Shape;
		lock (_pendingHostCallbacks)
		{
			if (_pointerShapeQueued)
			{
				return;
			}
			_pointerShapeQueued = true;
		}
		PostToHost(() =>
		{
			lock (_pendingHostCallbacks)
			{
				_pointerShapeQueued = false;
			}
			Cursor cursor = MapPointerShape(_pendingPointerShape);
			if (cursor == null)
			{
				if (!_shapeOverridden)
				{
					return;
				}
				cursor = _preShapeCursor;
				_preShapeCursor = null;
				_shapeOverridden = false;
			}
			else if (!_shapeOverridden)
			{
				_preShapeCursor = (_cursorOverridden ? _savedCursor : Cursor);
				_shapeOverridden = true;
			}
			if (_cursorOverridden)
			{
				_savedCursor = cursor;
			}
			else
			{
				SetCurrentValue(InputElement.CursorProperty, cursor);
			}
		});
	}

	private static Cursor? MapPointerShape(string? shape)
	{
		if (shape != null)
		{
			switch (shape.Length)
			{
			case 7:
			{
				char c = shape[0];
				if (c != 'd')
				{
					if (c != 'n')
					{
						if (c != 'p' || !(shape == "pointer"))
						{
							break;
						}
						return new Cursor(StandardCursorType.Hand);
					}
					if (!(shape == "no-drop"))
					{
						break;
					}
					goto IL_04a0;
				}
				if (!(shape == "default"))
				{
					break;
				}
				return global::Avalonia.Input.Cursor.Default;
			}
			case 4:
			{
				char c = shape[0];
				if ((uint)c <= 104u)
				{
					if (c == 'c')
					{
						if (!(shape == "cell"))
						{
							break;
						}
						goto IL_0497;
					}
					if (c != 'g')
					{
						if (c != 'h' || !(shape == "help"))
						{
							break;
						}
						return new Cursor(StandardCursorType.Help);
					}
					if (!(shape == "grab"))
					{
						break;
					}
				}
				else
				{
					if (c != 'm')
					{
						if (c != 't')
						{
							if (c != 'w' || !(shape == "wait"))
							{
								break;
							}
							goto IL_048e;
						}
						if (!(shape == "text"))
						{
							break;
						}
						goto IL_0471;
					}
					if (!(shape == "move"))
					{
						break;
					}
				}
				goto IL_04a9;
			}
			case 8:
			{
				char c = shape[0];
				if ((uint)c <= 110u)
				{
					if (c != 'e')
					{
						if (c != 'g')
						{
							if (c != 'n' || !(shape == "n-resize"))
							{
								break;
							}
							goto IL_04b2;
						}
						if (!(shape == "grabbing"))
						{
							break;
						}
						goto IL_04a9;
					}
					if (!(shape == "e-resize"))
					{
						break;
					}
				}
				else
				{
					if (c == 'p')
					{
						if (!(shape == "progress"))
						{
							break;
						}
						goto IL_048e;
					}
					if (c == 's')
					{
						if (!(shape == "s-resize"))
						{
							break;
						}
						goto IL_04b2;
					}
					if (c != 'w' || !(shape == "w-resize"))
					{
						break;
					}
				}
				goto IL_04bb;
			}
			case 9:
			{
				char c = shape[0];
				if ((uint)c <= 101u)
				{
					if (c != 'c')
					{
						if (c != 'e' || !(shape == "ew-resize"))
						{
							break;
						}
						goto IL_04bb;
					}
					if (!(shape == "crosshair"))
					{
						break;
					}
					goto IL_0497;
				}
				if (c != 'n')
				{
					if (c != 's')
					{
						break;
					}
					if (shape == "sw-resize")
					{
						goto IL_04c4;
					}
					if (!(shape == "se-resize"))
					{
						break;
					}
					goto IL_04ce;
				}
				switch (shape)
				{
				case "ns-resize":
					break;
				case "ne-resize":
					goto IL_04c4;
				case "nw-resize":
					goto IL_04ce;
				default:
					goto end_IL_0010;
				}
				goto IL_04b2;
			}
			case 11:
			{
				char c = shape[1];
				if (c != 'e')
				{
					if (c != 'o')
					{
						if (c != 'w' || !(shape == "nwse-resize"))
						{
							break;
						}
						goto IL_04ce;
					}
					if (!(shape == "not-allowed"))
					{
						break;
					}
					goto IL_04a0;
				}
				if (!(shape == "nesw-resize"))
				{
					break;
				}
				goto IL_04c4;
			}
			case 10:
			{
				char c = shape[0];
				if (c != 'a')
				{
					if (c != 'c')
					{
						if (c != 'r' || !(shape == "row-resize"))
						{
							break;
						}
						goto IL_04b2;
					}
					if (!(shape == "col-resize"))
					{
						break;
					}
					goto IL_04bb;
				}
				if (!(shape == "all-scroll"))
				{
					break;
				}
				goto IL_04a9;
			}
			case 1:
			{
				char c = shape[0];
				if ((uint)c <= 110u)
				{
					if (c != 'e' && c != 'n')
					{
						break;
					}
				}
				else if (c != 's' && c != 'w')
				{
					break;
				}
				goto IL_04d8;
			}
			case 2:
			{
				char c = shape[0];
				if (c != 'n')
				{
					if (c != 's' || (!(shape == "sw") && !(shape == "se")))
					{
						break;
					}
				}
				else if (!(shape == "ne") && !(shape == "nw"))
				{
					break;
				}
				goto IL_04d8;
			}
			case 13:
				{
					if (!(shape == "vertical-text"))
					{
						break;
					}
					goto IL_0471;
				}
				IL_04ce:
				return new Cursor(StandardCursorType.TopLeftCorner);
				IL_048e:
				return new Cursor(StandardCursorType.Wait);
				IL_04c4:
				return new Cursor(StandardCursorType.TopRightCorner);
				IL_04bb:
				return new Cursor(StandardCursorType.SizeWestEast);
				IL_04a0:
				return new Cursor(StandardCursorType.No);
				IL_04a9:
				return new Cursor(StandardCursorType.SizeAll);
				IL_04d8:
				return new Cursor(StandardCursorType.Arrow);
				IL_0497:
				return new Cursor(StandardCursorType.Cross);
				IL_04b2:
				return new Cursor(StandardCursorType.SizeNorthSouth);
				IL_0471:
				return new Cursor(StandardCursorType.Ibeam);
				end_IL_0010:
				break;
			}
		}
		return null;
	}

	private int PointerColumn(double x)
	{
		if (!(_charWidth > 0.0))
		{
			return 0;
		}
		return Math.Clamp((int)((x - Math.Max(0.0, GutterWidth)) / _charWidth), 0, Math.Max(0, _terminal.Cols - 1));
	}

	private int PointerRow(double y)
	{
		if (!(_charHeight > 0.0))
		{
			return 0;
		}
		return Math.Clamp((int)(y / _charHeight), 0, Math.Max(0, _terminal.Rows - 1));
	}

	private int PointerPixelX(double x)
	{
		if (!(_charWidth > 0.0))
		{
			return 0;
		}
		return ScaleToCellPixels((x - Math.Max(0.0, GutterWidth)) / _charWidth, _terminal.Cols, _terminal.Options.CellWidthPixels);
	}

	private int PointerPixelY(double y)
	{
		if (!(_charHeight > 0.0))
		{
			return 0;
		}
		return ScaleToCellPixels(y / _charHeight, _terminal.Rows, _terminal.Options.CellHeightPixels);
	}

	private static int ScaleToCellPixels(double cells, int count, int cellPixels)
	{
		cellPixels = Math.Max(1, cellPixels);
		int max = Math.Max(0, count * cellPixels - 1);
		return Math.Clamp((int)(Math.Max(0.0, cells) * (double)cellPixels), 0, max);
	}

	public void WaitForExit(int ms)
	{
		_ptyConnection?.WaitForExit(ms);
	}

	protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
	{
		base.OnDetachedFromLogicalTree(e);
		_topLevel = null;
		if (_disposed)
		{
			return;
		}
		if (_terminal == null)
		{
			if (!_suppressCleanupOnDetach)
			{
				CleanupProcess();
			}
			return;
		}
		UnsubscribeTerminalEvents();
		_atomicUpdate = false;
		if (!_suppressCleanupOnDetach)
		{
			CleanupProcess();
		}
	}

	protected override void OnPointerExited(PointerEventArgs e)
	{
		base.OnPointerExited(e);
		_lastReportedMotion = null;
		ClearHoveredUrl();
		if (!_shapeOverridden && Cursor != null)
		{
			SetCurrentValue(InputElement.CursorProperty, null);
		}
	}

	private async Task SendToPtyAsync(string data, CancellationToken ct = default(CancellationToken))
	{
		InputSent?.Invoke(this, data);
		IPtyConnection ptyConnection = _ptyConnection;
		if (ptyConnection == null || string.IsNullOrEmpty(data))
		{
			return;
		}
		await _semaphore.WaitAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			if (_ptyConnection == ptyConnection)
			{
				byte[] bytes = Utf8NoBom.GetBytes(data);
				await ptyConnection.WriterStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(continueOnCapturedContext: false);
				await ptyConnection.WriterStream.FlushAsync(ct).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		catch (Exception)
		{
		}
		finally
		{
			_semaphore.Release();
		}
	}

	public void AttachConnection(IPtyConnection connection)
	{
		ArgumentNullException.ThrowIfNull(connection, "connection");
		CleanupProcess();
		_externalConnection = true;
		_processCts = new CancellationTokenSource();
		long sessionId = InstallConnection(connection);
		connection.ProcessExited += OnPtyProcessExited;
		byte[] array = PendingHandoverBytes.ClaimOnAttach(connection);
		if (array != null && array.Length > 0)
		{
			bool shellReadyPosted = true;
			ConsumeOutputChunk(array, ref shellReadyPosted, _processCts.Token, sessionId);
		}
		CancellationToken readerToken = _processCts.Token;
		_readLoopTask = Task.Factory.StartNew(() => ReadPtyOutputAsync(connection, sessionId, readerToken), CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
	}

	public IPtyConnection? DetachConnection()
	{
		IPtyConnection ptyConnection;
		lock (_exitGate)
		{
			ptyConnection = _ptyConnection;
		}
		if (ptyConnection == null)
		{
			return null;
		}
		_externalConnection = true;
		Task readLoopTask = _readLoopTask;
		_readLoopTask = null;
		PendingHandoverBytes.NoteDetached(ptyConnection);
		CleanupProcess();
		if (ptyConnection.SupportsCancellableRead)
		{
			try
			{
				readLoopTask?.Wait(TimeSpan.FromSeconds(2L));
			}
			catch (AggregateException)
			{
			}
		}
		return ptyConnection;
	}

	public async Task LaunchProcess()
	{
		if (Design.IsDesignMode)
		{
			return;
		}
		CleanupProcess();
		_externalConnection = false;
		try
		{
			_processCts = new CancellationTokenSource();
			string text = Process;
			if (string.IsNullOrEmpty(text))
			{
				text = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash");
			}
			string text2 = StartingDirectory ?? Environment.CurrentDirectory;
			SetAndRaise(CurrentDirectoryProperty, ref _currentDirectory, text2);
			PtyOptions ptyOptions = new PtyOptions
			{
				Name = text,
				Cols = _terminal.Cols,
				Rows = _terminal.Rows,
				Cwd = text2,
				App = text,
				VerbatimCommandLine = VerbatimCommandLine
			};
			Dictionary<string, string> dictionary = ((EnvironmentVariables == null) ? new Dictionary<string, string>() : new Dictionary<string, string>(EnvironmentVariables));
			if (!dictionary.ContainsKey("TERM"))
			{
				dictionary["TERM"] = "xterm-256color";
			}
			if (!dictionary.ContainsKey("COLORTERM"))
			{
				dictionary["COLORTERM"] = "truecolor";
			}
			ptyOptions.Environment = dictionary;
			if (ProcessArgs != null && ProcessArgs.Count > 0)
			{
				ptyOptions.CommandLine = ProcessArgs.ToArray();
			}
			IPtyConnection spawned = await PtyProvider.SpawnAsync(ptyOptions, _processCts.Token);
			long spawnedSessionId = InstallConnection(spawned);
			spawned.ProcessExited += OnPtyProcessExited;
			TaskCompletionSource readerUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_readLoopTask = Task.Factory.StartNew(() => ReadPtyOutputAsync(spawned, spawnedSessionId, _processCts.Token, readerUp), CancellationToken.None, TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default).Unwrap();
			await Task.WhenAny(readerUp.Task, Task.Delay(TimeSpan.FromSeconds(5L))).ConfigureAwait(continueOnCapturedContext: true);
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
			{
				_terminal.WriteLine("Error launching process: " + ex2.Message + "\n");
			});
		}
	}

	public virtual async Task LaunchProcess(string? startingDirectory, string process, params string[] args)
	{
		StartingDirectory = startingDirectory;
		Process = process;
		ProcessArgs = args ?? Array.Empty<string>();
		await LaunchProcess();
	}

	private void ConsumeOutputChunk(ReadOnlyMemory<byte> chunk, ref bool shellReadyPosted, CancellationToken cancellationToken, long sessionId)
	{
		if (OutputReceived != null)
		{
			if (_outputOnReadTask)
			{
				try
				{
					OutputReceived?.Invoke(this, new OutputReceivedEventArgs(chunk)
					{
						SessionId = sessionId
					});
				}
				catch
				{
				}
			}
			else
			{
				global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
				{
					CancellationTokenSource? processCts = _processCts;
					if (processCts == null || processCts.Token != cancellationToken)
					{
						return;
					}
					try
					{
						OutputReceived?.Invoke(this, new OutputReceivedEventArgs(chunk)
						{
							SessionId = sessionId
						});
					}
					catch
					{
					}
				});
			}
		}
		int oldMax = MaxScrollback;
		int oldY = _terminal.Buffer.ViewportY;
		_followBottom = _isAlternateBuffer || (_autoScroll && _terminal.Buffer.IsAtBottom);
		long timestamp = Stopwatch.GetTimestamp();
		lock (_terminalLock)
		{
			long timestamp2 = Stopwatch.GetTimestamp();
			Interlocked.Increment(ref _liveWriteGeneration);
			_bufferWriteInProgress = true;
			try
			{
				_terminal.Write(chunk.Span);
			}
			finally
			{
				_bufferWriteInProgress = false;
				Interlocked.Add(ref Counters.WriteTicks, Stopwatch.GetTimestamp() - timestamp2);
				Interlocked.Add(ref Counters.LockWaitTicks, timestamp2 - timestamp);
				Interlocked.Add(ref Counters.Bytes, chunk.Length);
				Interlocked.Increment(ref Counters.Chunks);
			}
			int num = _terminal.Buffer.YBase + _terminal.Buffer.Y;
			if (num != _lastOutputRow)
			{
				_lastOutputRow = num;
				_inputStartPending = !_semanticPrompt;
			}
			if (!_atomicUpdate)
			{
				_frameCapture.PublishThrottled(_terminal, Interlocked.Read(in _liveWriteGeneration));
			}
		}
		if (!shellReadyPosted)
		{
			shellReadyPosted = true;
			global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				CancellationTokenSource? processCts = _processCts;
				if (processCts != null && !(processCts.Token != cancellationToken))
				{
					ShellReady?.Invoke(this, EventArgs.Empty);
				}
			});
		}
		if (!_isAlternateBuffer)
		{
			if (_followBottom)
			{
				_terminal.Buffer.ScrollToBottom();
			}
			else if (!_autoScroll && _terminal.Buffer.ViewportY != oldY)
			{
				_terminal.Buffer.ViewportY = Math.Min(oldY, MaxScrollback);
			}
			int newY = _terminal.Buffer.ViewportY;
			int newMax = MaxScrollback;
			if (oldMax != newMax || oldY != newY)
			{
				global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
				{
					if (oldMax != newMax)
					{
						RaisePropertyChanged(MaxScrollbackProperty, oldMax, newMax);
					}
					if (oldY != newY)
					{
						RaisePropertyChanged(ViewportYProperty, oldY, newY);
					}
				});
			}
		}
		NotifyInputMethodCoalesced();
		if (Interlocked.CompareExchange(ref _animationSyncQueued, 1, 0) == 0)
		{
			global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				Interlocked.Exchange(ref _animationSyncQueued, 0);
				SyncAnimationClock();
			});
		}
		RequestPaint();
	}

	private void NotifyInputMethodCoalesced()
	{
		if (!_imeFocused)
		{
			return;
		}
		long timestamp = Stopwatch.GetTimestamp();
		long num = Interlocked.Read(in _imeNotifiedAt);
		if ((num == 0L || !(Stopwatch.GetElapsedTime(num, timestamp) < ImeNotifyInterval)) && Interlocked.CompareExchange(ref _imeNotifyQueued, 1, 0) == 0)
		{
			Interlocked.Exchange(ref _imeNotifiedAt, timestamp);
			global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				Interlocked.Exchange(ref _imeNotifyQueued, 0);
				_inputMethodClient?.NotifyCursorRectangleChanged();
				_inputMethodClient?.NotifySurroundingTextChanged();
			});
		}
	}

	private async Task ReadPtyOutputAsync(IPtyConnection connection, long sessionId, CancellationToken cancellationToken, TaskCompletionSource? up = null)
	{
		up?.TrySetResult();
		try
		{
			byte[] buffer = new byte[262144];
			bool shellReadyPosted = false;
			while (!cancellationToken.IsCancellationRequested && _ptyConnection == connection)
			{
				int num;
				if (connection.SupportsCancellableRead)
				{
					try
					{
						num = await connection.ReaderStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
					}
					catch (OperationCanceledException)
					{
						break;
					}
				}
				else
				{
					num = connection.ReaderStream.Read(buffer, 0, buffer.Length);
				}
				if (_ptyConnection != connection)
				{
					if (num > 0)
					{
						PendingHandoverBytes.TryPark(connection, buffer.AsSpan(0, num));
					}
					break;
				}
				if (num == 0)
				{
					bool flag = false;
					try
					{
						flag = connection.WaitForExit(1000);
					}
					catch
					{
					}
					if (!flag)
					{
						ReapInBackground(connection);
					}
					if (flag && TryClaimExit(connection, out var exitSessionId))
					{
						int exitCode = connection.ExitCode;
						await WriteMessageAsync(TerminalMessageEventArgs.ProcessExited(exitCode, exitSessionId));
						await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
						{
							ProcessExited?.Invoke(this, new ProcessExitedEventArgs(exitCode)
							{
								SessionId = exitSessionId
							});
						});
					}
					break;
				}
				ReadOnlyMemory<byte> chunk = ((OutputReceived != null) ? new ReadOnlyMemory<byte>(buffer.AsSpan(0, num).ToArray()) : ((ReadOnlyMemory<byte>)buffer.AsMemory(0, num)));
				ConsumeOutputChunk(chunk, ref shellReadyPosted, cancellationToken, sessionId);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception exception)
		{
			if (_ptyConnection != connection || _processExitHandled != 0)
			{
				return;
			}
			await WriteMessageAsync(TerminalMessageEventArgs.ReadError(exception, sessionId));
		}
	}

	private async Task WriteMessageAsync(TerminalMessageEventArgs message)
	{
		if (WritingMessage == null)
		{
			WriteOwnLine(message.Text);
			return;
		}
		await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
		{
			try
			{
				WritingMessage?.Invoke(this, message);
			}
			catch (Exception)
			{
				message.Handled = false;
				message.Text = message.DefaultText;
			}
			if (!message.Handled && message.SessionId == SessionId)
			{
				WriteOwnLine(message.Text);
			}
		});
	}

	private long InstallConnection(IPtyConnection? connection)
	{
		lock (_exitGate)
		{
			_ptyConnection = connection;
			Interlocked.Exchange(ref _processExitHandled, 0);
			_sessionId = ((connection == null) ? 0 : (++_sessionCounter));
			return _sessionId;
		}
	}

	private bool TryClaimExit(IPtyConnection connection, out long sessionId)
	{
		lock (_exitGate)
		{
			sessionId = 0L;
			if (_ptyConnection != connection)
			{
				return false;
			}
			if (Interlocked.Exchange(ref _processExitHandled, 1) != 0)
			{
				return false;
			}
			sessionId = _sessionId;
			return true;
		}
	}

	private void ReapInBackground(IPtyConnection connection)
	{
		Task.Run(async () =>
		{
			DateTime deadline = DateTime.UtcNow.AddMilliseconds(30000.0);
			bool reaped = false;
			while (DateTime.UtcNow < deadline)
			{
				if (Volatile.Read(in _processExitHandled) != 0)
				{
					return;
				}
				try
				{
					if (connection.WaitForExit(100))
					{
						reaped = true;
						break;
					}
				}
				catch
				{
					return;
				}
				await Task.Yield();
			}
			if (TryClaimExit(connection, out var exitSessionId))
			{
				int? code = null;
				if (reaped)
				{
					try
					{
						code = connection.ExitCode;
					}
					catch
					{
					}
				}
				await WriteMessageAsync(TerminalMessageEventArgs.ProcessExited(code, exitSessionId));
				await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
				{
					EventHandler<ProcessExitedEventArgs>? eventHandler = ProcessExited;
					if (eventHandler != null)
					{
						TerminalView sender = this;
						ProcessExitedEventArgs e;
						if (code.HasValue)
						{
							int valueOrDefault = code.GetValueOrDefault();
							e = new ProcessExitedEventArgs(valueOrDefault)
							{
								SessionId = exitSessionId
							};
						}
						else
						{
							e = ProcessExitedEventArgs.UnknownCode(exitSessionId);
						}
						eventHandler(sender, e);
					}
				});
			}
		});
	}

	private void OnPtyProcessExited(object? sender, PtyExitedEventArgs e)
	{
		long exitSessionId;
		if (sender is IPtyConnection connection)
		{
			if (!TryClaimExit(connection, out exitSessionId))
			{
				return;
			}
		}
		else
		{
			if (Interlocked.Exchange(ref _processExitHandled, 1) != 0)
			{
				return;
			}
			exitSessionId = SessionId;
		}
		ReportAsync();
		async Task ReportAsync()
		{
			await WriteMessageAsync(TerminalMessageEventArgs.ProcessExited(e.ExitCode, exitSessionId));
			await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
			{
				ProcessExitedEventArgs e2 = new ProcessExitedEventArgs(e.ExitCode)
				{
					SessionId = exitSessionId
				};
				ProcessExited?.Invoke(this, e2);
			});
		}
	}

	private void CleanupProcess()
	{
		_processCts?.Cancel();
		ReleaseImageBitmaps();
		if (_ptyConnection != null)
		{
			try
			{
				_ptyConnection.ProcessExited -= OnPtyProcessExited;
				if (!_externalConnection)
				{
					_ptyConnection.Kill();
					_ptyConnection.Dispose();
				}
			}
			catch
			{
			}
			finally
			{
				InstallConnection(null);
			}
		}
		_processCts?.Dispose();
		_processCts = null;
	}

	private void RenderSearchHighlights(DrawingContext context, int viewportY, double scale)
	{
		if (_search == null || _search.Count == 0)
		{
			return;
		}
		for (int i = 0; i < _terminal.Rows; i++)
		{
			int num = viewportY + i;
			double num2 = RowCellScale(num);
			ReadOnlySpan<SearchHit> readOnlySpan = _search.HitsOnRow(num);
			for (int j = 0; j < readOnlySpan.Length; j++)
			{
				SearchHit searchHit = readOnlySpan[j];
				IBrush brush = ((searchHit.MatchId == _currentMatchId) ? SearchCurrentBrush : SearchHighlightBrush);
				if (brush != null)
				{
					int cols = (int)((double)_terminal.Cols / num2);
					if (ClampSpanToGrid(searchHit.Column, searchHit.EndColumn, cols, out var clampedStart, out var clampedEnd))
					{
						double num3 = Snap((double)clampedStart * _charWidth * num2, scale);
						double num4 = Snap((double)clampedEnd * _charWidth * num2, scale);
						double num5 = Snap((double)i * _charHeight, scale);
						double num6 = Snap((double)(i + 1) * _charHeight, scale);
						context.FillRectangle(brush, new Rect(num3, num5, Math.Max(0.0, num4 - num3), Math.Max(0.0, num6 - num5)));
					}
				}
			}
		}
	}

	private void RequestPaint()
	{
		if (_atomicUpdate)
		{
			if (Stopwatch.GetElapsedTime(_atomicUpdateStartedAt) < AtomicUpdateTimeout)
			{
				return;
			}
			_atomicUpdate = false;
		}
		this.RequestInvalidate();
	}

	private void SyncPaletteToBrushes()
	{
		if (_terminal != null)
		{
			if (Foreground is ISolidColorBrush solidColorBrush)
			{
				_terminal.Colors.SetForeground(ToRgb(solidColorBrush.Color));
			}
			if (Background is ISolidColorBrush solidColorBrush2)
			{
				_terminal.Colors.SetBackground(ToRgb(solidColorBrush2.Color));
			}
		}
		static int ToRgb(Color c)
		{
			return (c.R << 16) | (c.G << 8) | c.B;
		}
	}

	private static bool IsBlankRun(string text)
	{
		for (int i = 0; i < text.Length; i++)
		{
			if (text[i] != ' ')
			{
				return false;
			}
		}
		return text.Length > 0;
	}

	private void InvalidateRunCaches()
	{
		if (_terminal == null)
		{
			return;
		}
		for (int i = 0; i < _terminal.Buffer.Length; i++)
		{
			BufferLine line = _terminal.Buffer.GetLine(i);
			if (line != null)
			{
				line.Cache = null;
			}
		}
		_frameCapture.InvalidateAll();
		InvalidateVisual();
	}

	private void DrawGutter(DrawingContext context, double gutter, double scale)
	{
		if (gutter <= 0.0 || _charHeight <= 0.0)
		{
			return;
		}
		CircularList<BufferLine> lines = _terminal.Buffer.Lines;
		int viewportY = _terminal.Buffer.ViewportY;
		for (int i = 0; i < _terminal.Rows; i++)
		{
			int num = viewportY + i;
			if (num < 0 || num >= lines.Length)
			{
				continue;
			}
			BufferLine bufferLine = lines[num];
			if (bufferLine == null || !bufferLine.HasMarks)
			{
				continue;
			}
			int? num2 = null;
			bool flag = false;
			foreach (LineMark mark in bufferLine.Marks)
			{
				if (mark.Kind != ShellIntegrationMark.PromptStart && mark.Kind != ShellIntegrationMark.CommandFinished)
				{
					continue;
				}
				flag = true;
				if (mark.Kind != ShellIntegrationMark.CommandFinished)
				{
					continue;
				}
				int? exitCode = mark.ExitCode;
				if (exitCode.HasValue)
				{
					int valueOrDefault = exitCode.GetValueOrDefault();
					int valueOrDefault2 = num2.GetValueOrDefault();
					if (!num2.HasValue)
					{
						valueOrDefault2 = valueOrDefault;
						num2 = valueOrDefault2;
					}
				}
			}
			if (flag)
			{
				IBrush brush;
				if (num2.HasValue)
				{
					brush = ((num2.GetValueOrDefault() != 0) ? GutterFailureBrush : GutterSuccessBrush);
				}
				else
				{
					brush = null;
				}
				IBrush brush2 = brush;
				if (brush2 == null)
				{
					brush2 = GutterPromptBrush;
				}
				if (brush2 != null)
				{
					double num3 = Snap((double)i * _charHeight, scale);
					double num4 = Snap((double)(i + 1) * _charHeight, scale);
					context.FillRectangle(brush2, new Rect(0.0, num3, gutter, Math.Max(0.0, num4 - num3)));
				}
			}
		}
	}

	internal static bool ClampSpanToGrid(int start, int end, int cols, out int clampedStart, out int clampedEnd)
	{
		clampedStart = Math.Clamp(start, 0, cols);
		clampedEnd = Math.Clamp(end, clampedStart, cols);
		return clampedEnd > clampedStart;
	}

	internal static double FitWidth(double x, double measured, double right)
	{
		return Math.Min(measured, Math.Max(0.0, right - x));
	}

	public override void Render(DrawingContext context)
	{
		_sizedBlockDraws.Clear();
		_frontImageDraws.Clear();
		_palette = _terminal.Colors.Take();
		_boldIsBright = _terminal.Options.DrawBoldTextInBrightColors;
		_minimumContrast.SnapshotRatio(_terminal.Options.MinimumContrastRatio);
		IBrush brush = GetValue(BackgroundProperty);
		if (BufferCellExtensions.IsFullyOpaque(brush))
		{
			brush = new SolidColorBrush(BufferCellExtensions.FromRgb(_terminal.ReverseVideo ? _palette.Foreground : _palette.Background));
		}
		if (brush != null)
		{
			context.FillRectangle(brush, new Rect(Bounds.Size));
		}
		double renderScale = RenderScale;
		double num = Math.Max(0.0, GutterWidth);
		DrawGutter(context, num, renderScale);
		using ((num > 0.0) ? context.PushTransform(Matrix.CreateTranslation(num, 0.0)) : default(DrawingContext.PushedState))
		{
			int viewportY = _terminal.Buffer.ViewportY;
			int rows = _terminal.Rows;
			int num2 = viewportY;
			int num3 = Math.Min(_terminal.Buffer.Length, num2 + rows);
			CapturedFrame capturedFrame = _frameCapture.PinForRender(num2, _terminal.Cols, rows, Interlocked.Read(in _liveWriteGeneration), _atomicUpdate, _bufferWriteInProgress);
			PictureLayer lastPictureLayer = _lastPictureLayer;
			if (lastPictureLayer != null && lastPictureLayer.Unsupported)
			{
				_picturesUnsupported = true;
				_lastPictureLayer = null;
				RequestPaint();
			}
			try
			{
				if (_terminal.Buffer.HasMultiRowSizedRuns)
				{
					for (int i = 1; i < 7; i++)
					{
						int num4 = viewportY - i;
						if (num4 < 0)
						{
							break;
						}
						BufferLine bufferLine = ((num4 < _terminal.Buffer.Length) ? _terminal.Buffer.GetLine(num4) : null);
						if (bufferLine == null || !bufferLine.HasSizedRuns)
						{
							continue;
						}
						double num5 = Snap((double)(-i) * _charHeight, renderScale);
						double num6 = Snap((double)(-i + 1) * _charHeight, renderScale);
						foreach (LineSizedRun sizedRun in bufferLine.SizedRuns)
						{
							if (sizedRun.Rows > i)
							{
								_sizedBlockDraws.Add(new SizedBlockDraw(bufferLine, sizedRun, num5, Math.Max(0.0, num6 - num5)));
							}
						}
					}
				}
				for (int j = num2; j < num3 && j < _terminal.Buffer.Length; j++)
				{
					BufferLine bufferLine2 = capturedFrame?.LineAt(j) ?? _terminal.Buffer.GetLine(j);
					if (bufferLine2 != null)
					{
						int num7 = j - num2;
						double num8 = Snap((double)num7 * _charHeight, renderScale);
						double num9 = Snap((double)(num7 + 1) * _charHeight, renderScale);
						double rowHeight = Math.Max(0.0, num9 - num8);
						LineAttribute lineAttribute = bufferLine2.LineAttribute;
						if (lineAttribute == LineAttribute.DoubleWidth || lineAttribute == LineAttribute.DoubleHeightTop || lineAttribute == LineAttribute.DoubleHeightBottom)
						{
							RenderDoubleWidthLine(context, bufferLine2, num7, num8, rowHeight, lineAttribute, renderScale);
						}
						else
						{
							RenderNormalLine(context, bufferLine2, num7, num8, rowHeight, renderScale);
						}
					}
				}
				BufferLine statusLine = _statusLine;
				if (statusLine != null && _charHeight > 0.0)
				{
					double num10 = Snap((double)_terminal.Rows * _charHeight, renderScale);
					double num11 = Snap((double)(_terminal.Rows + 1) * _charHeight, renderScale);
					RenderNormalLine(context, statusLine, _terminal.Rows, num10, Math.Max(0.0, num11 - num10), renderScale);
				}
				RenderSizedBlocks(context, renderScale);
				RenderFrontImages(context, renderScale);
				RenderSearchHighlights(context, viewportY, renderScale);
				RenderHoveredUrl(context, viewportY, renderScale);
				RenderSelection(context, viewportY, renderScale);
				RenderCursor(context, viewportY, renderScale);
				RenderPreeditText(context, viewportY, renderScale);
			}
			catch (Exception)
			{
			}
		}
	}

	private void RenderNormalLine(DrawingContext context, BufferLine line, int screenY, double startYPos, double rowHeight, double scale)
	{
		List<CachedTextRun> list = ((!_terminal.ReverseVideo) ? (line.Cache as List<CachedTextRun>) : null);
		if (list == null)
		{
			list = CollectLineRuns(line, startYPos, rowHeight);
		}
		DrawLineRuns(context, list, startYPos, rowHeight, scale, deferFrontImages: true);
	}

	private void RenderFrontImages(DrawingContext context, double scale)
	{
		if (TryRenderFrontPicturesOnSkia(context, scale))
		{
			return;
		}
		foreach (FrontImageDraw frontImageDraw in _frontImageDraws)
		{
			CachedTextRun run = frontImageDraw.Run;
			if (run.Background != null)
			{
				double num = Snap((double)run.StartX * _charWidth, scale);
				double num2 = Snap((double)(run.StartX + run.CellCount) * _charWidth, scale);
				context.FillRectangle(run.Background, new Rect(num, frontImageDraw.StartYPos, Math.Max(0.0, num2 - num), frontImageDraw.RowHeight));
			}
			if (run.IsStrip)
			{
				DrawStripRun(context, run, frontImageDraw.StartYPos, frontImageDraw.RowHeight, scale);
			}
			else
			{
				DrawImageRun(context, run, frontImageDraw.StartYPos, frontImageDraw.RowHeight, scale);
			}
		}
	}

	private bool TryRenderFrontPicturesOnSkia(DrawingContext context, double scale)
	{
		//IL_013c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0161: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0310: Unknown result type (might be due to invalid IL or missing references)
		//IL_0312: Unknown result type (might be due to invalid IL or missing references)
		//IL_0316: Unknown result type (might be due to invalid IL or missing references)
		//IL_034d: Unknown result type (might be due to invalid IL or missing references)
		//IL_034f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0353: Unknown result type (might be due to invalid IL or missing references)
		if (_picturesUnsupported)
		{
			return false;
		}
		long frame = ++_pictureFrame;
		List<PictureDraw> list = new List<PictureDraw>();
		Dictionary<(int, TerminalImage, Rect), PictureGroup> dictionary = new Dictionary<(int, TerminalImage, Rect), PictureGroup>();
		List<PictureGroup> list2 = new List<PictureGroup>();
		foreach (FrontImageDraw frontImageDraw in _frontImageDraws)
		{
			CachedTextRun run = frontImageDraw.Run;
			if (run.Background != null)
			{
				double num = Snap((double)run.StartX * _charWidth, scale);
				double num2 = Snap((double)(run.StartX + run.CellCount) * _charWidth, scale);
				context.FillRectangle(run.Background, new Rect(num, frontImageDraw.StartYPos, Math.Max(0.0, num2 - num), frontImageDraw.RowHeight));
			}
			if (run.IsStrip)
			{
				SixelStrip strip = run.Strip;
				SharedSkImage strip2 = _pictures.GetStrip(strip, frame);
				if (strip2 != null)
				{
					double num3 = Snap((double)run.CellCount * _charWidth, scale);
					list.Add(new PictureDraw(strip2.AddRef(), new SKRect(0f, 0f, (float)(run.CellCount * strip.CellWidth), (float)strip.CellHeight), new SKRect(0f, (float)frontImageDraw.StartYPos, (float)num3, (float)(frontImageDraw.StartYPos + frontImageDraw.RowHeight)), null));
				}
			}
			else
			{
				if (!run.IsImage)
				{
					continue;
				}
				LinePlacement? placement = run.Placement;
				if (!placement.HasValue)
				{
					continue;
				}
				LinePlacement valueOrDefault = placement.GetValueOrDefault();
				if (TryPlanImageBlit(run, frontImageDraw.StartYPos, frontImageDraw.RowHeight, _charWidth, _charHeight, scale, out var _, out var destination, out var unifiedDest))
				{
					(int, TerminalImage, Rect) key = (valueOrDefault.Serial, run.Image, unifiedDest);
					if (!dictionary.TryGetValue(key, out var value))
					{
						value = (dictionary[key] = new PictureGroup
						{
							Image = run.Image,
							Mapping = unifiedDest,
							ZIndex = valueOrDefault.ZIndex,
							Serial = valueOrDefault.Serial
						});
						list2.Add(value);
					}
					value.Clips.Add(destination);
				}
			}
		}
		list2.Sort((PictureGroup a, PictureGroup b) => (a.ZIndex == b.ZIndex) ? a.Serial.CompareTo(b.Serial) : a.ZIndex.CompareTo(b.ZIndex));
		foreach (PictureGroup item in list2)
		{
			SharedSkImage sharedSkImage = _pictures.Get(item.Image, frame);
			if (sharedSkImage == null)
			{
				continue;
			}
			SKRect source2 = new SKRect(0f, 0f, (float)item.Image.PixelWidth, (float)item.Image.PixelHeight);
			SKRect destination2 = ToSK(item.Mapping);
			if (OneRectangle(item.Clips, out var union))
			{
				list.Add(new PictureDraw(sharedSkImage.AddRef(), source2, destination2, ToSK(union)));
				continue;
			}
			foreach (Rect clip in item.Clips)
			{
				list.Add(new PictureDraw(sharedSkImage.AddRef(), source2, destination2, ToSK(clip)));
			}
		}
		_pictures.Sweep(frame);
		if (list.Count == 0)
		{
			return true;
		}
		PictureLayer pictureLayer = new PictureLayer(list, new Rect(Bounds.Size));
		context.Custom(pictureLayer);
		_lastPictureLayer = pictureLayer;
		return true;
	}

	internal static bool OneRectangle(List<Rect> rects, out Rect union)
	{
		union = rects[0];
		if (rects.Count == 1)
		{
			return true;
		}
		rects.Sort((Rect a, Rect b) => a.Top.CompareTo(b.Top));
		for (int num = 1; num < rects.Count; num++)
		{
			Rect rect = rects[num - 1];
			Rect rect2 = rects[num];
			if (rect2.Left != rect.Left || rect2.Right != rect.Right || rect2.Top != rect.Bottom)
			{
				return false;
			}
		}
		union = new Rect(rects[0].Left, rects[0].Top, rects[0].Width, rects[rects.Count - 1].Bottom - rects[0].Top);
		return true;
	}

	private static SKRect ToSK(Rect rect)
	{
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		return new SKRect((float)rect.X, (float)rect.Y, (float)rect.Right, (float)rect.Bottom);
	}

	private void DrawStripRun(DrawingContext context, CachedTextRun run, double startYPos, double rowHeight, double scale)
	{
		if (_imageRenderingUnavailable)
		{
			return;
		}
		SixelStrip strip = run.Strip;
		Bitmap orCreateStripBitmap = GetOrCreateStripBitmap(strip);
		if (orCreateStripBitmap == null)
		{
			return;
		}
		double width = Snap((double)run.CellCount * _charWidth, scale);
		Rect sourceRect = new Rect(0.0, 0.0, run.CellCount * strip.CellWidth, strip.CellHeight);
		Rect destRect = new Rect(0.0, startYPos, width, rowHeight);
		try
		{
			context.DrawImage(orCreateStripBitmap, sourceRect, destRect);
		}
		catch (Exception exception) when (IndicatesNoRasterBackend(exception))
		{
			_imageRenderingUnavailable = true;
		}
		catch (Exception)
		{
		}
	}

	internal Bitmap? GetOrCreateStripBitmap(SixelStrip strip)
	{
		long version = strip.Version;
		if (_stripBitmaps.TryGetValue(strip, out CachedStripBitmap value) && value.Version == version && value.Bitmap != null)
		{
			return value.Bitmap;
		}
		Bitmap bitmap;
		try
		{
			bitmap = CreateStripBitmap(strip);
		}
		catch (Exception)
		{
			return null;
		}
		if (value == null)
		{
			value = new CachedStripBitmap();
			_stripBitmaps.Add(strip, value);
		}
		try
		{
			value.Bitmap?.Dispose();
		}
		catch
		{
		}
		value.Bitmap = bitmap;
		value.Version = ((strip.Version == version) ? version : (-1));
		return bitmap;
	}

	internal static Bitmap CreateStripBitmap(SixelStrip strip)
	{
		int num = Math.Max(1, strip.PaintedColumns);
		int num2 = num * strip.CellWidth * 4;
		int num3 = num2 * strip.PixelHeight;
		byte[] array = ArrayPool<byte>.Shared.Rent(num3);
		nint num4 = Marshal.AllocHGlobal(num3);
		try
		{
			strip.CopyPixelsTo(array, num2, num);
			Marshal.Copy(array, 0, num4, num3);
			return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, num4, new PixelSize(num * strip.CellWidth, strip.PixelHeight), new Vector(96.0, 96.0), num2);
		}
		finally
		{
			Marshal.FreeHGlobal(num4);
			ArrayPool<byte>.Shared.Return(array);
		}
	}

	private void DrawLineRuns(DrawingContext context, List<CachedTextRun> textRuns, double startYPos, double rowHeight, double scale, bool deferFrontImages = false)
	{
		foreach (CachedTextRun textRun in textRuns)
		{
			if (deferFrontImages)
			{
				if (textRun.IsStrip)
				{
					goto IL_0048;
				}
				if (textRun.IsImage)
				{
					LinePlacement? placement = textRun.Placement;
					if (placement.HasValue && placement.GetValueOrDefault().ZIndex >= 0)
					{
						goto IL_0048;
					}
				}
			}
			double num = Snap((double)textRun.StartX * _charWidth, scale);
			double num2 = Snap((double)(textRun.StartX + textRun.CellCount) * _charWidth, scale);
			Rect rect = new Rect(num, startYPos, Math.Max(0.0, num2 - num), rowHeight);
			Point point = new Point(num, startYPos);
			if (textRun.Background != null)
			{
				context.FillRectangle(textRun.Background, rect);
			}
			if (textRun.IsStrip)
			{
				DrawStripRun(context, textRun, startYPos, rowHeight, scale);
			}
			else if (textRun.IsImage)
			{
				DrawImageRun(context, textRun, startYPos, rowHeight, scale);
			}
			else if (textRun.IsBlockArt)
			{
				DrawBlocks(context, textRun, startYPos, rowHeight, scale);
			}
			else if (textRun.Glyphs != null)
			{
				DrawGlyphs(context, textRun, point);
			}
			else if (textRun.Text != null)
			{
				context.DrawText(textRun.Text, point);
			}
			if (textRun.UnderlineStyle != UnderlineStyle.None)
			{
				DrawUnderline(context, textRun, point, rect.Width, rowHeight);
			}
			continue;
			IL_0048:
			_frontImageDraws.Add(new FrontImageDraw(textRun, startYPos, rowHeight));
		}
	}

	private void DrawBlocks(DrawingContext context, CachedTextRun run, double startYPos, double rowHeight, double scale)
	{
		IBrush foreground = run.Foreground;
		if (foreground == null || run.Blocks == null)
		{
			return;
		}
		for (int i = 0; i < run.Blocks.Length; i++)
		{
			if (!BlockGlyphs.TryGet(run.Blocks[i], out BlockGlyphs.Shape[] shapes))
			{
				continue;
			}
			int num = run.StartX + i;
			BlockGlyphs.Shape[] array = shapes;
			for (int j = 0; j < array.Length; j++)
			{
				BlockGlyphs.Shape shape = array[j];
				if (shape.IsPolygon)
				{
					DrawBlockPolygon(context, foreground, shape.Points, num, startYPos, rowHeight, scale);
					continue;
				}
				double num2 = Snap(((double)num + (double)(int)shape.X0 / 24.0) * _charWidth, scale);
				double num3 = Snap(((double)num + (double)(int)shape.X1 / 24.0) * _charWidth, scale);
				double num4 = Snap(startYPos + (double)(int)shape.Y0 * rowHeight / 24.0, scale);
				double num5 = Snap(startYPos + (double)(int)shape.Y1 * rowHeight / 24.0, scale);
				if (num3 <= num2 && shape.X1 > shape.X0)
				{
					num3 = num2 + 1.0 / scale;
				}
				if (num5 <= num4 && shape.Y1 > shape.Y0)
				{
					num5 = num4 + 1.0 / scale;
				}
				if (num3 <= num2 || num5 <= num4)
				{
					continue;
				}
				Rect rect = new Rect(num2, num4, num3 - num2, num5 - num4);
				if (shape.Alpha >= 1f)
				{
					context.FillRectangle(foreground, rect);
					continue;
				}
				using (context.PushOpacity(shape.Alpha))
				{
					context.FillRectangle(foreground, rect);
				}
			}
		}
	}

	private void DrawBlockPolygon(DrawingContext context, IBrush brush, byte[] points, int col, double startYPos, double rowHeight, double scale)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(Vertex(0));
			for (int i = 2; i < points.Length; i += 2)
			{
				streamGeometryContext.LineTo(Vertex(i));
			}
			streamGeometryContext.EndFigure(isClosed: true);
		}
		context.DrawGeometry(brush, null, streamGeometry);
		Point Vertex(int num)
		{
			return new Point(Snap(((double)col + (double)(int)points[num] / 24.0) * _charWidth, scale), Snap(startYPos + (double)(int)points[num + 1] * rowHeight / 24.0, scale));
		}
	}

	private List<CachedTextRun> CollectLineRuns(BufferLine line, double startYPos, double rowHeight)
	{
		int count = _sizedBlockDraws.Count;
		int num = 1;
		List<CachedTextRun> list;
		bool hasSizedRuns;
		while (true)
		{
			if (_sizedBlockDraws.Count > count)
			{
				_sizedBlockDraws.RemoveRange(count, _sizedBlockDraws.Count - count);
			}
			object obj = (line.Cache = new object());
			list = BuildLineRuns(line, startYPos, rowHeight, out hasSizedRuns);
			if (line.Cache == obj)
			{
				break;
			}
			if (num >= 3)
			{
				line.Cache = null;
				RequestPaint();
				return list;
			}
			num++;
		}
		line.Cache = ((hasSizedRuns || _terminal.ReverseVideo) ? null : list);
		return list;
	}

	private static List<LinePlacement> OrderedPlacements(BufferLine line)
	{
		if (!line.HasImages)
		{
			return EmptyPlacements;
		}
		IReadOnlyList<LinePlacement> placements = line.Placements;
		if (placements.Count == 1)
		{
			return new List<LinePlacement>(placements);
		}
		return placements.OrderBy((LinePlacement p) => p.ZIndex).ToList();
	}

	private static TerminalImage? ImageFor(BufferLine line, LinePlacement placement)
	{
		foreach (TerminalImage image in line.Images)
		{
			if (image.Id == placement.ImageId)
			{
				return image;
			}
		}
		return null;
	}

	private void AppendImageRun(BufferLine line, LinePlacement placement, List<CachedTextRun> textRuns, List<LinePlacement> alreadyPainted)
	{
		TerminalImage terminalImage = ImageFor(line, placement);
		if (terminalImage != null)
		{
			int num = Math.Max(0, placement.Column);
			int num2 = Math.Min(placement.EndColumn, Math.Min(line.Length, _terminal.Cols));
			int num3 = num2 - num;
			if (num3 > 0)
			{
				BufferCell cell = line[num];
				IBrush backgroundBrush = cell.GetBackgroundBrush(_palette, Background);
				IBrush background = ((placement.Kind != PlacementKind.Sixel && (placement.Kind != PlacementKind.Kitty || placement.ZIndex < 0) && cell.GetBackgroundColor(_palette).HasValue && !OverlapsAny(alreadyPainted, num, num2)) ? backgroundBrush : null);
				textRuns.Add(new CachedTextRun(null, num, num3, background, placement, terminalImage));
			}
		}
	}

	private static void DrawUnderline(DrawingContext context, CachedTextRun run, Point position, double width, double cellHeight)
	{
		IBrush underlineBrush = run.UnderlineBrush;
		if (underlineBrush == null || width <= 0.0)
		{
			return;
		}
		double num = Math.Max(1.0, cellHeight / 14.0);
		double x = position.X;
		double num2 = position.Y + cellHeight - num * 2.0;
		switch (run.UnderlineStyle)
		{
		case UnderlineStyle.Double:
			context.FillRectangle(underlineBrush, new Rect(x, num2 - num, width, num));
			context.FillRectangle(underlineBrush, new Rect(x, num2 + num, width, num));
			break;
		case UnderlineStyle.Curly:
		{
			double amplitude = num * 1.5;
			double num4 = ((run.CellCount > 0) ? (width / (double)run.CellCount) : width);
			double period = Math.Max(4.0, num4 / 2.0);
			Geometry geometry = run.UnderlineGeometry;
			if (geometry == null)
			{
				double num5 = period / 2.0;
				double Wave(double dx) => amplitude * Math.Sin((x + dx) / period * Math.PI * 2.0);
				StreamGeometry streamGeometry = new StreamGeometry();
				using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
				{
					streamGeometryContext.BeginFigure(new Point(0.0, Wave(0.0)), isFilled: false);
					double num6 = (Math.Floor(x / num5) + 1.0) * num5 - x;
					if (num6 > 0.0 && num6 < width)
					{
						streamGeometryContext.QuadraticBezierTo(new Point(num6 / 2.0, Wave(0.0) + (Wave(num6) - Wave(0.0)) / 2.0 + LobeSign(x, num5) * amplitude / 2.0), new Point(num6, Wave(num6)));
					}
					double num7;
					for (num7 = Math.Max(0.0, num6); num7 + num5 <= width; num7 += num5)
					{
						streamGeometryContext.QuadraticBezierTo(new Point(num7 + num5 / 2.0, LobeSign(x + num7, num5) * amplitude * 2.0), new Point(num7 + num5, 0.0));
					}
					if (num7 < width)
					{
						streamGeometryContext.QuadraticBezierTo(new Point(num7 + (width - num7) / 2.0, LobeSign(x + num7, num5) * amplitude / 2.0 + Wave(width) / 2.0), new Point(width, Wave(width)));
					}
					streamGeometryContext.EndFigure(isClosed: false);
				}
				geometry = (run.UnderlineGeometry = streamGeometry);
			}
			IPen pen3 = run.UnderlinePen ?? (run.UnderlinePen = new ImmutablePen(underlineBrush.ToImmutable(), num));
			using (context.PushTransform(Matrix.CreateTranslation(x, num2)))
			{
				context.DrawGeometry(null, pen3, geometry);
				break;
			}
		}
		case UnderlineStyle.Dotted:
		case UnderlineStyle.Dashed:
		{
			IPen pen = run.UnderlinePen;
			if (pen == null)
			{
				double[] array = ((run.UnderlineStyle != UnderlineStyle.Dotted) ? new double[2] { 3.0, 2.0 } : new double[2] { 1.0, 1.0 });
				double num3 = (array[0] + array[1]) * num;
				double offset = x % num3 / num;
				pen = (run.UnderlinePen = new ImmutablePen(underlineBrush.ToImmutable(), num, new ImmutableDashStyle(array, offset)));
			}
			double y = num2 + num / 2.0;
			context.DrawLine(pen, new Point(x, y), new Point(x + width, y));
			break;
		}
		default:
			context.FillRectangle(underlineBrush, new Rect(x, num2, width, num));
			break;
		}
	}

	private static double LobeSign(double absoluteX, double halfPeriod)
	{
		if (Math.Floor(absoluteX / halfPeriod) % 2.0 != 0.0)
		{
			return -1.0;
		}
		return 1.0;
	}

	private void DrawImageRun(DrawingContext context, CachedTextRun run, double startYPos, double rowHeight, double scale)
	{
		if (_imageRenderingUnavailable || !TryPlanImageBlit(run, startYPos, rowHeight, _charWidth, _charHeight, scale, out var _, out var destination, out var unifiedDest))
		{
			return;
		}
		Bitmap orCreateBitmap = GetOrCreateBitmap(run.Image);
		if (orCreateBitmap == null)
		{
			return;
		}
		try
		{
			using (context.PushClip(destination))
			{
				context.DrawImage(orCreateBitmap, new Rect(0.0, 0.0, run.Image.PixelWidth, run.Image.PixelHeight), unifiedDest);
			}
		}
		catch (Exception exception) when (IndicatesNoRasterBackend(exception))
		{
			_imageRenderingUnavailable = true;
		}
		catch (Exception)
		{
			if (_imageBitmaps.TryGetValue(run.Image, out CachedBitmap value))
			{
				try
				{
					value.Bitmap?.Dispose();
				}
				catch
				{
				}
				value.Bitmap = null;
			}
		}
	}

	internal static bool IndicatesNoRasterBackend(Exception exception)
	{
		if (exception is NotImplementedException || exception is PlatformNotSupportedException || exception is NotSupportedException)
		{
			return true;
		}
		return false;
	}

	private static bool CoveredByBackdrop(List<LinePlacement> painted, int start, int end)
	{
		return OverlapsAny(painted, start, end);
	}

	private static List<LinePlacement> BackdropPlacements(BufferLine line)
	{
		List<LinePlacement> list = OrderedPlacements(line);
		if (list.Count == 0 || list[0].ZIndex >= 0)
		{
			return EmptyPlacements;
		}
		List<LinePlacement> list2 = new List<LinePlacement>();
		foreach (LinePlacement item in list)
		{
			if (item.ZIndex >= 0)
			{
				break;
			}
			list2.Add(item);
		}
		return list2;
	}

	private static bool OverlapsAny(List<LinePlacement> earlier, int start, int end)
	{
		foreach (LinePlacement item in earlier)
		{
			if (item.Column < end && item.EndColumn > start)
			{
				return true;
			}
		}
		return false;
	}

	internal static bool TryPlanImageBlit(CachedTextRun run, double startYPos, double rowHeight, double charWidth, double charHeight, double scale, out Rect source, out Rect destination)
	{
		Rect unifiedDest;
		return TryPlanImageBlit(run, startYPos, rowHeight, charWidth, charHeight, scale, out source, out destination, out unifiedDest);
	}

	internal static bool TryPlanImageBlit(CachedTextRun run, double startYPos, double rowHeight, double charWidth, double charHeight, double scale, out Rect source, out Rect destination, out Rect unifiedDest)
	{
		source = default;
		destination = default;
		unifiedDest = default;
		LinePlacement? placement = run.Placement;
		if (placement.HasValue)
		{
			LinePlacement valueOrDefault = placement.GetValueOrDefault();
			if (run.Image != null)
			{
				if (run.CellCount <= 0 || charWidth <= 0.0 || charHeight <= 0.0)
				{
					return false;
				}
				if (valueOrDefault.Cols <= 0 || valueOrDefault.SrcWidth <= 0 || valueOrDefault.SrcHeight <= 0)
				{
					return false;
				}
				int num = Math.Min(run.CellCount, valueOrDefault.Cols);
				double num2 = (double)valueOrDefault.SrcWidth * (double)num / (double)valueOrDefault.Cols;
				if (num2 <= 0.0)
				{
					return false;
				}
				int num3 = ((run.Image.CellWidth <= 0) ? 1 : run.Image.CellWidth);
				int num4 = ((run.Image.CellHeight <= 0) ? 1 : run.Image.CellHeight);
				double num5 = (double)valueOrDefault.OffsetX / (double)num3 * charWidth;
				double num6 = (double)valueOrDefault.OffsetY / (double)num4 * charHeight;
				bool num7 = valueOrDefault.PxPerCellX > 0f || valueOrDefault.PxPerCellY > 0f;
				double num8 = ((valueOrDefault.PxPerCellX > 0f) ? valueOrDefault.PxPerCellX : ((float)num3));
				double num9 = ((valueOrDefault.PxPerCellY > 0f) ? valueOrDefault.PxPerCellY : ((float)num4));
				double num10 = (num7 ? ((double)num * charWidth) : (num2 / num8 * charWidth));
				double num11 = (num7 ? rowHeight : ((double)valueOrDefault.SrcHeight / num9 * rowHeight));
				double num12 = (double)run.StartX * charWidth;
				double val = (double)(run.StartX + num) * charWidth;
				double num13 = num12 + num5;
				double num14 = startYPos + num6;
				double num15 = Math.Max(num12, num13);
				double num16 = Math.Min(val, num13 + num10);
				double num17 = Math.Max(startYPos, num14);
				double num18 = Math.Min(startYPos + rowHeight, num14 + num11);
				if (num16 <= num15 || num18 <= num17)
				{
					return false;
				}
				double x = ((num15 == num13) ? ((double)valueOrDefault.SrcX) : ((double)valueOrDefault.SrcX + (num15 - num13) / num10 * num2));
				double y = ((num17 == num14) ? ((double)valueOrDefault.SrcY) : ((double)valueOrDefault.SrcY + (num17 - num14) / num11 * (double)valueOrDefault.SrcHeight));
				double width = ((num15 == num13 && num16 == num13 + num10) ? num2 : ((num16 - num15) / num10 * num2));
				double height = ((num17 == num14 && num18 == num14 + num11) ? ((double)valueOrDefault.SrcHeight) : ((num18 - num17) / num11 * (double)valueOrDefault.SrcHeight));
				double num19 = charWidth / num8;
				double num20 = charHeight / num9;
				double num21 = Math.Round(startYPos / charHeight) * charHeight;
				double num22 = num13 - (double)valueOrDefault.SrcX * num19;
				double num23 = num21 + num6 - (double)valueOrDefault.SrcY * num20;
				double num24 = Snap(num22, scale);
				double num25 = Snap(num23, scale);
				unifiedDest = new Rect(num24, num25, Snap(num22 + (double)run.Image.PixelWidth * num19, scale) - num24, Snap(num23 + (double)run.Image.PixelHeight * num20, scale) - num25);
				double num26 = Snap(num15, scale);
				double num27 = Snap(num16, scale);
				double num28 = Snap(num17, scale);
				double num29 = Snap(num18, scale);
				destination = new Rect(num26, num28, Math.Max(0.0, num27 - num26), Math.Max(0.0, num29 - num28));
				if (destination.Width <= 0.0 || destination.Height <= 0.0)
				{
					destination = default;
					return false;
				}
				source = new Rect(x, y, width, height);
				return true;
			}
		}
		return false;
	}

	internal Bitmap? GetOrCreateBitmap(TerminalImage image)
	{
		if (_imageBitmaps.TryGetValue(image, out CachedBitmap value))
		{
			if (value.FrameSerial == image.FrameSerial)
			{
				return value.Bitmap;
			}
			try
			{
				value.Bitmap?.Dispose();
			}
			catch
			{
			}
			value.Bitmap = TryCreateBitmap(image);
			value.FrameSerial = image.FrameSerial;
			return value.Bitmap;
		}
		Bitmap bitmap = TryCreateBitmap(image);
		_imageBitmaps.Add(image, new CachedBitmap
		{
			Bitmap = bitmap,
			FrameSerial = image.FrameSerial
		});
		return bitmap;
	}

	private static Bitmap? TryCreateBitmap(TerminalImage image)
	{
		try
		{
			return CreateBitmap(image);
		}
		catch (Exception)
		{
			return null;
		}
	}

	private void ReleaseImageBitmaps()
	{
		foreach (KeyValuePair<TerminalImage, CachedBitmap> item in (IEnumerable<KeyValuePair<TerminalImage, CachedBitmap>>)_imageBitmaps)
		{
			CachedBitmap value = item.Value;
			if (value != null)
			{
				value.Bitmap?.Dispose();
				value.Bitmap = null;
			}
		}
		_imageBitmaps.Clear();
		foreach (KeyValuePair<SixelStrip, CachedStripBitmap> item2 in (IEnumerable<KeyValuePair<SixelStrip, CachedStripBitmap>>)_stripBitmaps)
		{
			item2.Value.Bitmap?.Dispose();
			item2.Value.Bitmap = null;
		}
		_stripBitmaps.Clear();
		_pictures.Clear();
	}

	internal static Bitmap CreateBitmap(TerminalImage image)
	{
		int num = image.PixelWidth * 4;
		nint num2 = Marshal.AllocHGlobal(num * image.PixelHeight);
		try
		{
			CopyPixels(image, num2, num);
			return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Unpremul, num2, new PixelSize(image.PixelWidth, image.PixelHeight), new Vector(96.0, 96.0), num);
		}
		finally
		{
			Marshal.FreeHGlobal(num2);
		}
	}

	private static void DrawGlyphs(DrawingContext context, CachedTextRun run, Point position)
	{
		using (context.PushTransform(Matrix.CreateTranslation(position.X, position.Y)))
		{
			context.DrawGlyphRun(run.Foreground ?? Brushes.Transparent, run.Glyphs);
		}
	}

	internal GlyphRun? TryBuildGlyphRun(string text, int cellCount, FontStyle style, FontWeight weight)
	{
		if (!GlyphRunFastPathEnabled)
		{
			return null;
		}
		if (text.Length == 0 || _charWidth <= 0.0)
		{
			return null;
		}
		if (text.Length == 1 && cellCount == 2)
		{
			char c = text[0];
			if (char.IsSurrogate(c))
			{
				return null;
			}
			GlyphTypeface glyphTypeface = ResolveWideGlyphTypeface(c, style, weight, out var glyph);
			if (glyphTypeface == null || glyph == 0)
			{
				return null;
			}
			GlyphInfo[] glyphInfos = new GlyphInfo[1]
			{
				// r67.12：ink 行内垂直居中（对齐 MobaXterm；修复前 CJK 整块偏低 ~3px）
				new GlyphInfo(glyph, 0, _charWidth * 2.0, WideGlyphVerticalOffset(glyphTypeface, glyph, c))
			};
			return new GlyphRun(glyphTypeface, FontSize, text.AsMemory(), glyphInfos, new Point(0.0, _baseline));
		}
		if (text.Length != cellCount)
		{
			return null;
		}
		GlyphTypeface glyphTypeface2 = GlyphTypefaceFor(style, weight);
		if (glyphTypeface2 == null)
		{
			return null;
		}
		CharacterToGlyphMap characterToGlyphMap = glyphTypeface2.CharacterToGlyphMap;
		GlyphInfo[] array = new GlyphInfo[text.Length];
		for (int i = 0; i < text.Length; i++)
		{
			char c2 = text[i];
			if (char.IsSurrogate(c2))
			{
				return null;
			}
			if (!characterToGlyphMap.TryGetGlyph(c2, out var glyphId) || glyphId == 0)
			{
				return null;
			}
			array[i] = new GlyphInfo(glyphId, i, _charWidth);
		}
		return new GlyphRun(glyphTypeface2, FontSize, text.AsMemory(), array, new Point(0.0, _baseline));
	}

	private GlyphTypeface? GlyphTypefaceFor(FontStyle style, FontWeight weight)
	{
		if (_glyphTypefaces.TryGetValue((style, weight), out GlyphTypeface value))
		{
			return value;
		}
		GlyphTypeface glyphTypeface;
		try
		{
			glyphTypeface = new Typeface(FontFamily, style, weight).GlyphTypeface;
		}
		catch
		{
			glyphTypeface = null;
		}
		_glyphTypefaces[(style, weight)] = glyphTypeface;
		return glyphTypeface;
	}

			/// <summary>
		/// r67.12：宽（全角/CJK）字形的行内垂直居中偏移。主字体基线 _baseline 位于行高约 70% 处
		/// （Consolas 14.5px：行高 16.978、基线 ~12），而雅黑 CJK ink 重心在基线处，不修正则中文
		/// 整块比英文低 ~3px（用户对照 MobaXterm 截图反馈；MobaXterm 中文 ink 充满行框且居中）。
		/// 首选 1em 探针实测字形 ink 包围盒（与上游 fallback 拟合同一手法）；几何不可用的环境
		/// （如 headless，BuildGeometry 返回零宽占位框）退化为 em 盒中心（CJK 字体字形 ink 近似
		/// 位于 em 盒中心，用 FontMetrics.Ascent/Descent 计算）；两者都不可用返回零偏移
		/// （保持 4.1.7 基线钉扎行为，不丢字）。
		/// offset = 行中心 − ink 中心（均相对主字体基线，y 向下为正），按 (face,glyph) 缓存。
		/// 仅行重建时调用（线缓存），非每帧。
		/// </summary>
		private Vector WideGlyphVerticalOffset(GlyphTypeface typeface, ushort glyph, char c)
		{
			var key = (typeface, glyph);
			if (_wideVOffsets.TryGetValue(key, out var cached))
			{
				return cached;
			}
			double inkCenter = double.NaN;                       // em，相对基线，y 向下为正
			try
			{
				GlyphRun probe = new GlyphRun(typeface, 1.0, c.ToString().AsMemory(),
					new GlyphInfo[1] { new GlyphInfo(glyph, 0, 0.0) }, new Point(0.0, 0.0));
				if (probe.BuildGeometry() is { Bounds: { Width: > 0 } ink })
				{
					inkCenter = ink.Y + ink.Height / 2.0;
				}
			}
			catch
			{
				// 探针异常：走 em 盒退化
			}
			if (double.IsNaN(inkCenter))
			{
				try
				{
					var fm = typeface.Metrics;
					if (fm != null && fm.DesignEmHeight > 0)
					{
						inkCenter = -(fm.Ascent - fm.Descent) / 2.0 / fm.DesignEmHeight;
					}
				}
				catch
				{
					// 度量不可用：零偏移
				}
			}
			if (double.IsNaN(inkCenter))
			{
				// r67.12（r77）：几何/度量暂不可用时不缓存零偏移。
				// 旧行为把 default 写入 _wideVOffsets，使冷启动窗口期（字体文件
				// 未加载）建立的行缓存永久钉在 4.1.7 原生位（用户截图：整屏 CJK
				// 原生、底差 +5 签名）。本次仍按基线渲染（不丢字），下次行重建重试探针。
				return default(Vector);
			}
			double rowCenter = (_charHeight / 2.0 - _baseline) / FontSize;  // em，行中心相对基线
			var offset = new Vector(0.0, (rowCenter - inkCenter) * FontSize);
			if (_wideVOffsets.Count > 8192)
			{
				_wideVOffsets.Clear();
			}
			_wideVOffsets[key] = offset;
			return offset;
		}

		private GlyphTypeface? ResolveWideGlyphTypeface(char wide, FontStyle style, FontWeight weight, out ushort glyph)
	{
		glyph = 0;
		GlyphTypeface glyphTypeface = GlyphTypefaceFor(style, weight);
		if (glyphTypeface != null && glyphTypeface.CharacterToGlyphMap.TryGetGlyph(wide, out glyph) && glyph != 0 && glyphTypeface.TryGetGlyphMetrics(glyph, out var metrics))
		{
			return glyphTypeface;
		}
		if (_wideFallback.TryGetValue((style, weight), out GlyphTypeface value) && value != null && value.CharacterToGlyphMap.TryGetGlyph(wide, out glyph) && glyph != 0 && value.TryGetGlyphMetrics(glyph, out metrics))
		{
			return value;
		}
		string[] array = _fontFamilyChain.Split(',');
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length != 0)
			{
				GlyphTypeface glyphTypeface2;
				try
				{
					glyphTypeface2 = new Typeface(text, style, weight).GlyphTypeface;
				}
				catch
				{
					continue;
				}
				if (glyphTypeface2 != null && glyphTypeface2.CharacterToGlyphMap.TryGetGlyph(wide, out glyph) && glyph != 0 && glyphTypeface2.TryGetGlyphMetrics(glyph, out metrics))
				{
					_wideFallback[(style, weight)] = glyphTypeface2;
					return glyphTypeface2;
				}
			}
		}
		glyph = 0;
		return null;
	}

	private void RenderHoveredUrl(DrawingContext context, int viewportY, double scale)
	{
		HoveredUrl hoveredLink = _hoveredLink;
		if (hoveredLink == null)
		{
			return;
		}
		Pen pen = null;
		foreach (var segment in hoveredLink.Segments)
		{
			int num = segment.Line - viewportY;
			if (num >= 0 && num < _terminal.Rows)
			{
				double num2 = RowCellScale(segment.Line);
				double x = Snap((double)segment.StartCol * _charWidth * num2, scale);
				double x2 = Snap((double)(segment.EndCol + 1) * _charWidth * num2, scale);
				double y = Snap((double)(num + 1) * _charHeight - 1.0, scale);
				if (pen == null)
				{
					pen = new Pen(Foreground);
				}
				context.DrawLine(pen, new Point(x, y), new Point(x2, y));
			}
		}
	}

	private void RenderSizedBlocks(DrawingContext context, double scale)
	{
		if (_sizedBlockDraws.Count == 0)
		{
			return;
		}
		Rect clip = new Rect(0.0, 0.0, (double)_terminal.Cols * _charWidth, (double)_terminal.Rows * _charHeight);
		using (context.PushClip(clip))
		{
			foreach (SizedBlockDraw sizedBlockDraw in _sizedBlockDraws)
			{
				BufferLine line = sizedBlockDraw.Line;
				LineSizedRun run = sizedBlockDraw.Run;
				TextSizing sizing = run.Sizing;
				List<LinePlacement> painted = (_minimumContrast.Active ? BackdropPlacements(line) : EmptyPlacements);
				double num = ((sizing.Numerator > 0 && sizing.Denominator > 0) ? ((double)sizing.Numerator / (double)sizing.Denominator) : 1.0);
				double num2 = (double)sizing.Scale * num;
				if (num2 <= 0.0)
				{
					continue;
				}
				int num3 = run.Column;
				while (num3 < run.EndColumn && num3 < line.Length)
				{
					BufferCell cell = line[num3];
					if (cell.Width <= 0)
					{
						num3++;
						continue;
					}
					double num4 = Snap((double)num3 * _charWidth, scale);
					double num5 = Snap((double)(num3 + cell.Width) * _charWidth, scale);
					Rect rect = new Rect(num4, sizedBlockDraw.StartYPos, Math.Max(0.0, num5 - num4), sizedBlockDraw.RowHeight * (double)run.Rows);
					IBrush brush = cell.GetBackgroundBrush(_palette, Background);
					IBrush brush2 = cell.GetForegroundBrush(_palette, Foreground, BoldFont && _boldIsBright);
					bool flag = false;
					if (cell.Attributes.IsInverse())
					{
						IBrush brush3 = brush;
						IBrush brush4 = brush2;
						flag = !flag;
						brush = brush4;
						brush2 = brush3;
					}
					if (_terminal.ReverseVideo)
					{
						IBrush brush5 = brush;
						IBrush brush6 = brush2;
						flag = !flag;
						brush = brush6;
						brush2 = brush5;
					}
					if (_minimumContrast.Active && !CoveredByBackdrop(painted, num3, num3 + cell.Width) && !MinimumContrast.IsExemptRun(cell.Content) && brush2 is ISolidColorBrush solidColorBrush && brush is ISolidColorBrush solidColorBrush2 && BufferCellExtensions.IsFullyOpaque(brush))
					{
						Color color = _minimumContrast.Apply(solidColorBrush.Color, solidColorBrush2.Color);
						if (color != solidColorBrush.Color)
						{
							brush2 = new SolidColorBrush(color, solidColorBrush.Opacity);
						}
					}
					brush2 = cell.ApplyConceal(brush2);
					brush2 = cell.ApplyBlinkPhase(brush2, _cursorBlinkOn);
					if (flag || _terminal.ReverseVideo || cell.GetBackgroundColor(_palette).HasValue)
					{
						context.FillRectangle(brush, rect);
					}
					if (string.IsNullOrEmpty(cell.Content) || cell.Content == " ")
					{
						num3 += cell.Width;
						continue;
					}
					Typeface typeface = new Typeface(FontFamily, cell.GetFontStyle(), BoldFont ? cell.GetFontWeight() : FontWeight.Normal);
					FormattedText formattedText = new FormattedText(cell.Content, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, brush2);
					ApplyLigatureSetting(formattedText);
					double num6 = formattedText.Width * num2;
					double num7 = sizedBlockDraw.RowHeight * num2;
					double num8 = sizing.HorizontalAlignment switch
					{
						TextSizeHorizontalAlignment.Right => rect.Right - num6, 
						TextSizeHorizontalAlignment.Center => rect.X + (rect.Width - num6) / 2.0, 
						_ => rect.X, 
					};
					double num9 = sizing.VerticalAlignment switch
					{
						TextSizeVerticalAlignment.Bottom => rect.Bottom - num7, 
						TextSizeVerticalAlignment.Center => rect.Y + (rect.Height - num7) / 2.0, 
						_ => rect.Y, 
					};
					using (context.PushClip(rect))
					{
						Matrix matrix = Matrix.CreateTranslation(0.0 - num8, 0.0 - num9);
						Matrix matrix2 = Matrix.CreateScale(num2, num2);
						Matrix matrix3 = Matrix.CreateTranslation(num8, num9);
						using (context.PushTransform(matrix * matrix2 * matrix3))
						{
							context.DrawText(formattedText, new Point(num8, num9));
						}
					}
					num3 += cell.Width;
				}
			}
		}
	}

	private void RenderDoubleWidthLine(DrawingContext context, BufferLine line, int screenY, double startYPos, double rowHeight, LineAttribute lineAttr, double scale)
	{
		line.Cache = null;
		Rect clip = new Rect(0.0, startYPos, (double)_terminal.Cols * _charWidth, rowHeight);
		double xScale = 2.0;
		double yScale = (lineAttr.IsDoubleHeight() ? 2.0 : 1.0);
		double num = 0.0;
		if (lineAttr == LineAttribute.DoubleHeightBottom)
		{
			num = 0.0 - rowHeight;
		}
		using (context.PushClip(clip))
		{
			Matrix matrix = Matrix.CreateScale(xScale, yScale);
			Matrix matrix2 = Matrix.CreateTranslation(0.0, 0.0 - startYPos);
			Matrix matrix3 = Matrix.CreateTranslation(0.0, startYPos + num);
			Matrix matrix4 = matrix2 * matrix * matrix3;
			using (context.PushTransform(matrix4))
			{
				int num2 = (_terminal.Cols + 1) / 2;
				List<LinePlacement> list = OrderedPlacements(line);
				List<LinePlacement> list2 = new List<LinePlacement>();
				List<CachedTextRun> list3 = new List<CachedTextRun>();
				int i;
				for (i = 0; i < list.Count && list[i].ZIndex < 0; i++)
				{
					AppendImageRun(line, list[i], list3, list2);
					list2.Add(list[i]);
				}
				DrawLineRuns(context, list3, startYPos, rowHeight, scale);
				list3.Clear();
				int j = 0;
				while (j < num2 && j < line.Length)
				{
					BufferCell cell = line[j];
					string text = string.Empty;
					int num3 = 0;
					int num4 = 0;
					bool flag = CoveredByBackdrop(list2, j, j + Math.Max(1, cell.Width));
					if (cell.Width == 0)
					{
						j++;
						continue;
					}
					if (cell.Width == 1)
					{
						StringBuilder stringBuilder = new StringBuilder();
						num3 = 0;
						num4 = j;
						BufferCell bufferCell;
						for (; j < line.Length && j < num2; j += bufferCell.Width)
						{
							bufferCell = line[j];
							if (bufferCell.Width != 1 || bufferCell.Attributes != cell.Attributes || CoveredByBackdrop(list2, j, j + 1) != flag)
							{
								break;
							}
							stringBuilder.Append(bufferCell.Content);
							num3 += bufferCell.Width;
						}
						text = stringBuilder.ToString();
					}
					else if (cell.Width == 2)
					{
						text = cell.Content;
						num3 = cell.Width;
						num4 = j;
						j += cell.Width;
					}
					double num5 = Snap((double)num4 * _charWidth, scale);
					double num6 = Snap((double)(num4 + num3) * _charWidth, scale);
					Rect rect = new Rect(num5, startYPos, Math.Max(0.0, num6 - num5), rowHeight);
					IBrush brush = cell.GetBackgroundBrush(_palette, Background);
					IBrush brush2 = cell.GetForegroundBrush(_palette, Foreground, BoldFont && _boldIsBright);
					bool flag2 = false;
					if (cell.Attributes.IsInverse())
					{
						IBrush brush3 = brush;
						IBrush brush4 = brush2;
						flag2 = !flag2;
						brush = brush4;
						brush2 = brush3;
					}
					if (_terminal.ReverseVideo)
					{
						IBrush brush5 = brush;
						IBrush brush6 = brush2;
						flag2 = !flag2;
						brush = brush6;
						brush2 = brush5;
					}
					if (_minimumContrast.Active && !flag && !MinimumContrast.IsExemptRun(text) && brush2 is ISolidColorBrush solidColorBrush && brush is ISolidColorBrush solidColorBrush2 && BufferCellExtensions.IsFullyOpaque(brush))
					{
						Color color = _minimumContrast.Apply(solidColorBrush.Color, solidColorBrush2.Color);
						if (color != solidColorBrush.Color)
						{
							brush2 = new SolidColorBrush(color, solidColorBrush.Opacity);
						}
					}
					brush2 = cell.ApplyConceal(brush2);
					brush2 = cell.ApplyBlinkPhase(brush2, _cursorBlinkOn);
					Typeface typeface = new Typeface(FontFamily, cell.GetFontStyle(), BoldFont ? cell.GetFontWeight() : FontWeight.Normal);
					FormattedText formattedText = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, brush2);
					ApplyLigatureSetting(formattedText);
					TextDecorationCollection textDecorations = cell.GetTextDecorations();
					if (textDecorations != null)
					{
						formattedText.SetTextDecorations(textDecorations);
					}
					Point point = new Point(num5, startYPos);
					if ((flag2 || _terminal.ReverseVideo || cell.GetBackgroundColor(_palette).HasValue) && !flag)
					{
						context.FillRectangle(brush, rect);
					}
					context.DrawText(formattedText, point);
					UnderlineStyle underlineStyle = cell.Attributes.GetUnderlineStyle();
					if (underlineStyle != UnderlineStyle.None)
					{
						Color? underlineColor = cell.GetUnderlineColor(_palette);
						IBrush brush8;
						IBrush brush7;
						if (underlineColor.HasValue)
						{
							Color valueOrDefault = underlineColor.GetValueOrDefault();
							brush7 = new ImmutableSolidColorBrush(valueOrDefault);
							brush8 = brush7;
						}
						else
						{
							brush8 = brush2;
						}
						IBrush swappedForeground = brush8;
						swappedForeground = cell.ApplyBlinkPhase(swappedForeground, _cursorBlinkOn);
						int startX = num4;
						int cellCount = num3;
						UnderlineStyle underlineStyle2 = underlineStyle;
						brush7 = swappedForeground;
						CachedTextRun run = new CachedTextRun(null, startX, cellCount, null, null, null, underlineStyle2, brush7);
						DrawUnderline(context, run, point, Math.Max(0.0, num6 - num5), rowHeight);
					}
				}
				SixelStrip sixel = line.Sixel;
				if (sixel != null)
				{
					int num7 = Math.Min(sixel.PaintedColumns, Math.Min(line.Length, num2));
					if (num7 > 0)
					{
						SixelStrip strip = sixel;
						list3.Add(new CachedTextRun(null, 0, num7, null, null, null, UnderlineStyle.None, null, null, null, null, strip));
					}
				}
				for (; i < list.Count; i++)
				{
					AppendImageRun(line, list[i], list3, list2);
					list2.Add(list[i]);
				}
				DrawLineRuns(context, list3, startYPos, rowHeight, scale);
			}
		}
	}

	private void RenderSelection(DrawingContext context, int viewportY, double scale)
	{
		if (!_terminal.Selection.HasSelection)
		{
			return;
		}
		int rows = _terminal.Rows;
		int num = viewportY - _terminal.Buffer.YDisp;
		for (int i = 0; i < rows; i++)
		{
			int? num2 = null;
			int? num3 = null;
			for (int j = 0; j < _terminal.Cols; j++)
			{
				if (_terminal.Selection.IsCellSelected(j, i + num))
				{
					if (!num2.HasValue)
					{
						num2 = j;
					}
					num3 = j;
				}
				else if (num2.HasValue)
				{
					DrawSelectionRect(context, num2.Value, num3.Value + 1, i, scale, RowCellScale(viewportY + i));
					num2 = null;
					num3 = null;
				}
			}
			if (num2.HasValue)
			{
				DrawSelectionRect(context, num2.Value, num3.Value + 1, i, scale, RowCellScale(viewportY + i));
			}
		}
	}

	private int CellWidthAt(int absoluteRow, int col)
	{
		BufferLine line = _terminal.Buffer.GetLine(absoluteRow);
		if (line == null || col < 0 || col >= line.Length)
		{
			return 1;
		}
		return Math.Max(1, line.GetWidth(col));
	}

	private double RowCellScale(int absoluteRow)
	{
		BufferLine line = _terminal.Buffer.GetLine(absoluteRow);
		if (line == null)
		{
			return 1.0;
		}
		LineAttribute lineAttribute = line.LineAttribute;
		if (lineAttribute != LineAttribute.DoubleWidth && !lineAttribute.IsDoubleHeight())
		{
			return 1.0;
		}
		return 2.0;
	}

	private void DrawSelectionRect(DrawingContext context, int startX, int endX, int screenY, double scale, double cellScale)
	{
		double num = Snap((double)startX * _charWidth * cellScale, scale);
		double num2 = Snap((double)endX * _charWidth * cellScale, scale);
		double num3 = Snap((double)screenY * _charHeight, scale);
		double num4 = Snap((double)(screenY + 1) * _charHeight, scale);
		context.FillRectangle(rect: new Rect(num, num3, Math.Max(0.0, num2 - num), Math.Max(0.0, num4 - num3)), brush: SelectionBrush);
	}

	private void RenderCursor(DrawingContext context, int viewportY, double scale)
	{
		if (!IsLive || SuppressCursor || CaretHidden || !_terminal.CursorVisible || !_cursorBlinkOn)
		{
			return;
		}
		var (num, num2) = CaretPosition;
		if (num2 < viewportY || num2 >= viewportY + _terminal.Rows)
		{
			return;
		}
		int num3 = num2 - viewportY;
		double num4 = RowCellScale(num2);
		int num5 = Math.Max(1, CellWidthAt(num2, num));
		double num6 = Snap((double)num * _charWidth * num4, scale);
		double num7 = Snap((double)num3 * _charHeight, scale);
		double num8 = Snap((double)(num + num5) * _charWidth * num4, scale);
		double num9 = Snap((double)(num3 + 1) * _charHeight, scale);
		double num10 = Math.Max(0.0, num8 - num6);
		double num11 = Math.Max(0.0, num9 - num7);
		SolidColorBrush brush = new SolidColorBrush(CursorColor);
		switch (CursorStyle)
		{
		case CursorStyle.Block:
			if (IsFocused)
			{
				context.FillRectangle(brush, new Rect(num6, num7, num10, num11));
				BufferLine line = _terminal.Buffer.GetLine(num2);
				if (line != null && num < line.Length)
				{
					BufferCell cell = line[num];
					FormattedText text = new FormattedText(cell.Content ?? " ", typeface: new Typeface(FontFamily, FontStyle, FontWeight), foreground: cell.GetBackgroundBrush(_palette, Background), culture: CultureInfo.CurrentCulture, flowDirection: FlowDirection.LeftToRight, emSize: FontSize);
					context.DrawText(text, new Point(num6, num7));
				}
			}
			else
			{
				Pen pen = new Pen(brush);
				context.DrawRectangle(pen, new Rect(num6, num7, num10, num11));
			}
			break;
		case CursorStyle.Underline:
		{
			double num12 = Math.Min(2.0, num11);
			context.FillRectangle(brush, new Rect(num6, num7 + num11 - num12, num10, num12));
			break;
		}
		case CursorStyle.Bar:
		{
			double width = Math.Min(2.0, num10);
			context.FillRectangle(brush, new Rect(num6, num7, width, num11));
			break;
		}
		}
	}

	private static double Snap(double value, double scale)
	{
		return Math.Round(value * scale, MidpointRounding.AwayFromZero) / scale;
	}

	private void RenderPreeditText(DrawingContext context, int viewportY, double scale)
	{
		string text = _inputMethodClient?.PreeditText;
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		int x = _terminal.Buffer.X;
		int y = _terminal.Buffer.Y;
		int num = _terminal.Buffer.YBase + y;
		if (num < viewportY || num >= viewportY + _terminal.Rows)
		{
			return;
		}
		int num2 = num - viewportY;
		double num3 = RowCellScale(num);
		double num4 = Snap((double)x * _charWidth * num3, scale);
		double num5 = Snap((double)num2 * _charHeight, scale);
		double num6 = Snap((double)(num2 + 1) * _charHeight, scale) - num5;
		Typeface typeface = new Typeface(FontFamily, FontStyle, FontWeight);
		IBrush brush = GetValue(ForegroundProperty) ?? Brushes.White;
		IBrush brush2 = GetValue(BackgroundProperty) ?? Brushes.Black;
		IBrush brush3;
		if (!BufferCellExtensions.IsFullyOpaque(brush2))
		{
			brush3 = brush2;
		}
		else
		{
			IBrush brush4 = new SolidColorBrush(BufferCellExtensions.FromRgb(_palette.Background));
			brush3 = brush4;
		}
		IBrush brush5 = brush3;
		FormattedText formattedText = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, brush);
		ApplyLigatureSetting(formattedText);
		double num7 = FitWidth(num4, formattedText.Width * num3, (double)_terminal.Cols * _charWidth);
		if (num7 <= 0.0)
		{
			return;
		}
		using (context.PushClip(new Rect(num4, num5, num7, num6)))
		{
			context.FillRectangle(brush5, new Rect(num4, num5, num7, num6));
			if (num3 == 1.0)
			{
				context.DrawText(formattedText, new Point(num4, num5));
			}
			else
			{
				Matrix matrix = Matrix.CreateTranslation(0.0 - num4, 0.0 - num5);
				Matrix matrix2 = Matrix.CreateScale(num3, 1.0);
				Matrix matrix3 = Matrix.CreateTranslation(num4, num5);
				using (context.PushTransform(matrix * matrix2 * matrix3))
				{
					context.DrawText(formattedText, new Point(num4, num5));
				}
			}
			double y2 = num5 + num6 - Math.Max(1.0, scale);
			Pen pen = new Pen(brush, Math.Max(1.0, scale));
			context.DrawLine(pen, new Point(num4, y2), new Point(num4 + num7, y2));
		}
	}

	public void FollowTail()
	{
		if (!_isAlternateBuffer && _autoScroll)
		{
			_followBottom = true;
			int maxScrollback = MaxScrollback;
			if (ViewportY < maxScrollback)
			{
				ViewportY = maxScrollback;
			}
		}
	}

	public int FindInBuffer(string needle, SearchOptions options = default(SearchOptions))
	{
		if (_search == null)
		{
			_search = new BufferSearch(_terminal);
		}
		int result = _search.Find(needle, options);
		_currentMatchId = -1;
		InvalidateVisual();
		return result;
	}

	public bool FindNext()
	{
		return MoveSearch(next: true);
	}

	public bool FindPrevious()
	{
		return MoveSearch(next: false);
	}

	public void ClearSearch()
	{
		_search?.Clear();
		_currentMatchId = -1;
		InvalidateVisual();
	}

	private bool MoveSearch(bool next)
	{
		if (_search == null)
		{
			return false;
		}
		if (!(next ? _search.TryMoveNext(out var hit) : _search.TryMovePrevious(out hit)))
		{
			return false;
		}
		_currentMatchId = hit.MatchId;
		int viewportY = _terminal.Buffer.ViewportY;
		if (hit.BufferRow < viewportY || hit.BufferRow >= viewportY + _terminal.Rows)
		{
			ViewportY = Math.Clamp(hit.BufferRow - _terminal.Rows / 2, 0, Math.Max(0, MaxScrollback));
		}
		InvalidateVisual();
		return true;
	}

	public bool ScrollToPreviousPrompt()
	{
		if (!_terminal.TryFindPreviousPrompt(_terminal.Buffer.ViewportY, out var row))
		{
			return false;
		}
		ViewportY = row;
		return true;
	}

	public bool ScrollToNextPrompt()
	{
		if (!_terminal.TryFindNextPrompt(_terminal.Buffer.ViewportY, out var row))
		{
			return false;
		}
		ViewportY = row;
		return true;
	}

	public bool SelectCommandOutput(int bufferRow)
	{
		CircularList<BufferLine> lines = _terminal.Buffer.Lines;
		if (bufferRow < 0 || bufferRow >= lines.Length)
		{
			return false;
		}
		int num = -1;
		for (int num2 = bufferRow; num2 >= 0; num2--)
		{
			if (HasMark(lines[num2], ShellIntegrationMark.CommandExecuted))
			{
				num = num2 + 1;
				break;
			}
			if (num2 != bufferRow && HasMark(lines[num2], ShellIntegrationMark.PromptStart))
			{
				return false;
			}
		}
		if (num < 0)
		{
			return false;
		}
		int num3 = lines.Length - 1;
		for (int i = num; i < lines.Length; i++)
		{
			if (HasMark(lines[i], ShellIntegrationMark.PromptStart))
			{
				num3 = i - 1;
				break;
			}
		}
		if (num3 < num)
		{
			return false;
		}
		_terminal.Selection.StartSelection(0, num);
		_terminal.Selection.UpdateSelection(Math.Max(0, _terminal.Cols - 1), num3);
		_terminal.Selection.EndSelection();
		InvalidateVisual();
		return true;
	}

	private static bool HasMark(BufferLine? line, ShellIntegrationMark kind)
	{
		if (line == null || !line.HasMarks)
		{
			return false;
		}
		foreach (LineMark mark in line.Marks)
		{
			if (mark.Kind == kind)
			{
				return true;
			}
		}
		return false;
	}

	private int LineEndBoundary(int from, int cols)
	{
		int num = from / cols;
		BufferLine line = _terminal.Buffer.GetLine(_terminal.Buffer.ViewportY + num);
		if (line == null)
		{
			return from;
		}
		int num2 = -1;
		for (int i = 0; i < Math.Min(line.Length, cols); i++)
		{
			BufferCell bufferCell = line[i];
			if (!string.IsNullOrWhiteSpace(bufferCell.Content))
			{
				num2 = i + Math.Max(0, bufferCell.Width - 1);
			}
		}
		int num3 = num * cols + num2 + 1;
		if (num3 <= from)
		{
			return from;
		}
		return num3;
	}

	private int WordBoundary(int from, int direction, int cols, int lastBoundary)
	{
		int i = Math.Clamp(from, 0, lastBoundary);
		bool flag = false;
		if (direction < 0)
		{
			while (i > 0 && IsSeparator(CellAt(i - 1)))
			{
				i--;
			}
			while (i > 0 && !IsSeparator(CellAt(i - 1)))
			{
				i--;
				flag = true;
			}
		}
		else
		{
			for (; i < lastBoundary && IsSeparator(CellAt(i)); i++)
			{
			}
			while (i < lastBoundary && !IsSeparator(CellAt(i)))
			{
				i++;
				flag = true;
			}
		}
		if (!flag)
		{
			return from;
		}
		return i;
		(string? Content, int Width) CellAt(int boundary)
		{
			int num = boundary / cols;
			int num2 = boundary % cols;
			BufferLine line = _terminal.Buffer.GetLine(_terminal.Buffer.ViewportY + num);
			if (line == null || num2 < 0 || num2 >= line.Length)
			{
				return (Content: null, Width: 1);
			}
			return (Content: line[num2].Content, Width: line[num2].Width);
		}
		static bool IsSeparator((string? Content, int Width) cell)
		{
			if (cell.Width != 0)
			{
				return !IsWordChar(cell.Content);
			}
			return false;
		}
		static bool IsWordChar(string? content)
		{
			if (!string.IsNullOrEmpty(content))
			{
				if (!char.IsLetterOrDigit(content[0]))
				{
					return content[0] == '_';
				}
				return true;
			}
			return false;
		}
	}

	private void AnswerWindowInfo(TerminalEvents.WindowInfoRequestedEventArgs e)
	{
		WindowInfoRequestedEventArgs e2 = new WindowInfoRequestedEventArgs(e.Request)
		{
			RoutedEvent = WindowInfoRequestedEvent
		};
		RaiseEvent(e2);
		WindowInfoRequested?.Invoke(this, e2);
		if (e2.Handled)
		{
			e.Handled = true;
			e.IsIconified = e2.IsIconified;
			e.X = e2.X;
			e.Y = e2.Y;
			e.WidthPixels = e2.WidthPixels;
			e.HeightPixels = e2.HeightPixels;
			e.CellWidth = e2.CellWidth;
			e.CellHeight = e2.CellHeight;
			e.Title = e2.Title;
		}
	}

	private bool ShouldHandleSelection(global::Avalonia.Input.KeyModifiers modifiers)
	{
		bool flag = _terminal.MouseTrackingMode != MouseTrackingMode.None;
		bool flag2 = modifiers.HasFlag(global::Avalonia.Input.KeyModifiers.Shift) && !_terminal.ShiftEscape;
		return !flag | flag2;
	}

	private (string Text, List<(int Line, int Col)> Map)? BuildLogicalLine(int bufferLine)
	{
		TerminalBuffer buffer = _terminal.Buffer;
		if (bufferLine < 0 || bufferLine >= buffer.Length)
		{
			return null;
		}
		int num;
		for (num = bufferLine; num > 0; num--)
		{
			BufferLine? line = buffer.GetLine(num);
			if (line == null || !line.IsWrapped)
			{
				break;
			}
		}
		int i;
		for (i = bufferLine; i + 1 < buffer.Length; i++)
		{
			BufferLine? line2 = buffer.GetLine(i + 1);
			if (line2 == null || !line2.IsWrapped)
			{
				break;
			}
		}
		int cols = _terminal.Cols;
		StringBuilder stringBuilder = new StringBuilder(cols * (i - num + 1));
		List<(int, int)> list = new List<(int, int)>(cols * (i - num + 1));
		for (int j = num; j <= i; j++)
		{
			BufferLine line3 = buffer.GetLine(j);
			if (line3 == null)
			{
				continue;
			}
			for (int k = 0; k < cols; k++)
			{
				if (k >= line3.Length)
				{
					stringBuilder.Append(' ');
					list.Add((j, k));
					continue;
				}
				BufferCell bufferCell = line3[k];
				if (bufferCell.Width == 0)
				{
					continue;
				}
				string content = bufferCell.Content;
				if (string.IsNullOrEmpty(content))
				{
					stringBuilder.Append(' ');
					list.Add((j, k));
					continue;
				}
				stringBuilder.Append(content);
				for (int l = 0; l < content.Length; l++)
				{
					list.Add((j, k));
				}
			}
		}
		return (stringBuilder.ToString(), list);
	}

	private static string TrimUrlEnd(string url)
	{
		while (url.Length > 0)
		{
			char c = url[url.Length - 1];
			bool flag;
			switch (c)
			{
			case '!':
			case '"':
			case '\'':
			case ',':
			case '.':
			case ':':
			case ';':
			case '?':
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag)
			{
				url = url.Substring(0, url.Length - 1);
				continue;
			}
			char c2 = c switch
			{
				')' => '(', 
				']' => '[', 
				'}' => '{', 
				_ => '\0', 
			};
			if (c2 == '\0' || CountChar(url, c2) >= CountChar(url, c))
			{
				break;
			}
			url = url.Substring(0, url.Length - 1);
		}
		return url;
	}

	internal HoveredUrl? FindUrlAtColumn(int bufferLine, int col)
	{
		HoveredUrl hoveredUrl = FindHyperlinkAtColumn(bufferLine, col);
		if (hoveredUrl != null)
		{
			return hoveredUrl;
		}
		(string, List<(int, int)>)? tuple = BuildLogicalLine(bufferLine);
		if (!tuple.HasValue)
		{
			return null;
		}
		(string, List<(int, int)>) value = tuple.Value;
		string item = value.Item1;
		List<(int, int)> item2 = value.Item2;
		int num = -1;
		for (int i = 0; i < item2.Count; i++)
		{
			if (item2[i].Item1 == bufferLine && item2[i].Item2 == col)
			{
				num = i;
				break;
			}
		}
		if (num < 0)
		{
			return null;
		}
		foreach (Match item3 in UrlRegex.Matches(item))
		{
			string text = TrimUrlEnd(item3.Value);
			if (text.Length == 0)
			{
				continue;
			}
			int index = item3.Index;
			int num2 = item3.Index + text.Length - 1;
			if (num < index || num > num2)
			{
				continue;
			}
			List<(int, int, int)> list = new List<(int, int, int)>();
			for (int j = index; j <= num2 && j < item2.Count; j++)
			{
				var (num3, num4) = item2[j];
				if (list.Count > 0)
				{
					(int, int, int) tuple3 = list[list.Count - 1];
					if (tuple3.Item1 == num3)
					{
						list[list.Count - 1] = (num3, tuple3.Item2, Math.Max(tuple3.Item3, num4));
						continue;
					}
				}
				list.Add((num3, num4, num4));
			}
			return (list.Count > 0) ? new HoveredUrl(text, list) : null;
		}
		return null;
	}

	private HoveredUrl? FindHyperlinkAtColumn(int bufferLine, int col)
	{
		CircularList<BufferLine> lines = _terminal.Buffer.Lines;
		if (bufferLine < 0 || bufferLine >= lines.Length)
		{
			return null;
		}
		BufferLine bufferLine2 = lines[bufferLine];
		if (bufferLine2 == null || !bufferLine2.HasLinks)
		{
			return null;
		}
		if (!bufferLine2.TryGetLinkAt(col, out var link))
		{
			return null;
		}
		List<(int, int, int)> list = new List<(int, int, int)> { (bufferLine, link.Column, link.EndColumn - 1) };
		if (link.Id != null)
		{
			int num = bufferLine - 1;
			LineHyperlink span;
			while (num >= 0 && TryGetSpanWithId(lines[num], link.Id, out span))
			{
				list.Insert(0, (num, span.Column, span.EndColumn - 1));
				num--;
			}
			LineHyperlink span2;
			for (int i = bufferLine + 1; i < lines.Length && TryGetSpanWithId(lines[i], link.Id, out span2); i++)
			{
				list.Add((i, span2.Column, span2.EndColumn - 1));
			}
		}
		return new HoveredUrl(link.Url, list, fromSequence: true);
	}

	private static bool TryGetSpanWithId(BufferLine? line, string id, out LineHyperlink span)
	{
		if (line != null && line.HasLinks)
		{
			foreach (LineHyperlink link in line.Links)
			{
				if (string.Equals(link.Id, id, StringComparison.Ordinal))
				{
					span = link;
					return true;
				}
			}
		}
		span = default;
		return false;
	}

	private void UpdateTextMetrics()
	{
		double oldCharWidth = _charWidth;
		double oldCharHeight = _charHeight;
		double oldBaseline = _baseline;
		Typeface typeface = new Typeface(FontFamily, FontStyle, FontWeight);
		_fontFamilyChain = (((object)FontFamily == null) ? "monospace" : string.Join(",", FontFamily.FamilyNames));
		_measureText = new FormattedText("W", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize, Brushes.Black);
		// r67.12（r77）：瞬时测量为 0（布局/字体未就绪）时保留旧非零度量，
		// 避免 _charWidth<=0 使整行走 FormattedText 回退（CJK 原生位）并烘焙进行缓存。
		if (_measureText.Width > 0.0)
		{
			_charWidth = _measureText.Width;
		}
		if (_measureText.Height > 0.0)
		{
			_charHeight = _measureText.Height;
		}
		if (_measureText.Baseline > 0.0)
		{
			_baseline = _measureText.Baseline;
		}
		bool metricsChanged = Math.Abs(oldCharWidth - _charWidth) > 1e-6 || Math.Abs(oldCharHeight - _charHeight) > 1e-6 || Math.Abs(oldBaseline - _baseline) > 1e-6;
		_glyphTypefaces.Clear();
		_wideVOffsets.Clear();
		// r67.12（r77）：度量变化（含首次 0→就绪、字号缩放、窗口重排）时：
		// (1) _wideFallback 重新解析——冷启动期解析到的半就绪 CJK typeface 不再持久；
		// (2) 行 run 缓存（line.Cache）整体失效——烘焙了旧偏移/零偏移的 GlyphRun 重建。
		if (metricsChanged)
		{
			_wideFallback.Clear();
			InvalidateRunCaches();
		}
		PublishCellPixelSize();
		// r67.12（r77）：启动期预热 CJK 回退字体（强制字体文件加载），
		// 使 TUI 首帧 CJK 探针落在已就绪字体上（冷启动竞态的根因修复）。
		// 探针失败（字体未就绪）时不置完成位，安排有限次重试（≤5 次布局轮）。
		if (!_wideWarmupDone && _charWidth > 0.0)
		{
			bool warmupOk = false;
			try
			{
				ushort g = 0;
				if (ResolveWideGlyphTypeface('\u4E2D', FontStyle.Normal, FontWeight.Normal, out g) is { } wt && g != 0)
				{
					warmupOk = WideGlyphVerticalOffset(wt, g, '\u4E2D') != default;
				}
			}
			catch
			{
				// 预热异常：按失败处理，走重试。
			}
			if (warmupOk)
			{
				_wideWarmupDone = true;
			}
			else if (_wideWarmupRetries < 5)
			{
				_wideWarmupRetries++;
				global::Avalonia.Threading.Dispatcher.UIThread.Post(() => InvalidateMeasure(), global::Avalonia.Threading.DispatcherPriority.Background);
			}
			else
			{
				_wideWarmupDone = true; // 放弃重试：零偏移不缓存策略仍保证后续行重建时重试探针
			}
		}
	}

	private void PublishCellPixelSize()
	{
		if (_terminal != null && !(_charWidth <= 0.0) && !(_charHeight <= 0.0))
		{
			double renderScale = RenderScale;
			int num = Math.Max(1, (int)Math.Round(_charWidth * renderScale));
			int num2 = Math.Max(1, (int)Math.Round(_charHeight * renderScale));
			_terminal.Options.DisplayScale = renderScale;
			if (num != _terminal.Options.CellWidthPixels || num2 != _terminal.Options.CellHeightPixels)
			{
				_terminal.Options.CellWidthPixels = num;
				_terminal.Options.CellHeightPixels = num2;
				_terminal.NotifyTextAreaPixelsChanged();
			}
		}
	}

	protected override Size MeasureOverride(Size availableSize)
	{
		UpdateTextMetrics();
		int num = _terminal?.Cols ?? 80;
		int num2 = _terminal?.Rows ?? 24;
		double width = (double.IsInfinity(availableSize.Width) ? ((double)num * _charWidth + Math.Max(0.0, GutterWidth)) : availableSize.Width);
		double height = (double.IsInfinity(availableSize.Height) ? ((double)num2 * _charHeight) : availableSize.Height);
		return new Size(width, height);
	}
}
