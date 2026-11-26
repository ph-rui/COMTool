using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Porta.Pty;
using XTerm;
using XTerm.Common;
using XTerm.Options;
using XTerm.Search;

namespace Iciclecreek.Terminal;

public class TerminalControl : TemplatedControl, IDisposable
{
	private TerminalView? _terminalView;

	private ScrollBar? _scrollBar;

	private string? _currentDirectory;

	public static readonly StyledProperty<TextDecorationLocation?> TextDecorationsProperty;

	public static readonly StyledProperty<IBrush> SelectionBrushProperty;

	public static readonly StyledProperty<string> ProcessProperty;

	public static readonly StyledProperty<IList<string>> ProcessArgsProperty;

	public static readonly StyledProperty<string?> StartingDirectoryProperty;

	public static readonly DirectProperty<TerminalControl, string?> CurrentDirectoryProperty;

	public static readonly StyledProperty<int> BufferSizeProperty;

	public static readonly StyledProperty<TerminalOptions?> OptionsProperty;

	public static readonly StyledProperty<bool> ShowCaretOnClickProperty;

	public static readonly StyledProperty<bool> AutoCopyOnSelectionProperty;

	public static readonly StyledProperty<bool> VerbatimCommandLineProperty;

	public static readonly StyledProperty<IDictionary<string, string>?> EnvironmentVariablesProperty;

	public static readonly StyledProperty<bool> LigaturesProperty;

	public static readonly StyledProperty<bool> BoldFontProperty;

	public static readonly StyledProperty<bool> ConvertEolProperty;

	public static readonly StyledProperty<bool> AllowWindowOpsProperty;

	public static readonly StyledProperty<bool> UseSkiaRendererProperty;

	public static readonly StyledProperty<Color> CursorColorProperty;

	public static readonly StyledProperty<CursorStyle> CursorStyleProperty;

	public static readonly StyledProperty<bool> CursorBlinkProperty;

	public static readonly StyledProperty<int> CursorBlinkRateProperty;

	private static bool _stylesLoaded;

	public static readonly StyledProperty<ShortcutMode> ShortcutModeProperty;

	public static readonly StyledProperty<bool> SuppressCursorProperty;

	public static readonly StyledProperty<double> GutterWidthProperty;

	public static readonly StyledProperty<IBrush?> GutterPromptBrushProperty;

	public static readonly StyledProperty<IBrush?> GutterSuccessBrushProperty;

	public static readonly StyledProperty<IBrush?> GutterFailureBrushProperty;

	public static readonly StyledProperty<IBrush> SearchHighlightBrushProperty;

	public static readonly StyledProperty<IBrush> SearchCurrentBrushProperty;

	public static readonly StyledProperty<bool> OutputReceivedOnReadTaskProperty;

	public static readonly StyledProperty<bool> AutoScrollToBottomProperty;

	private bool _applyingScrollBarValue;

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

	public int BufferSize
	{
		get
		{
			return GetValue(BufferSizeProperty);
		}
		set
		{
			SetValue(BufferSizeProperty, value);
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

	public XTerm.Terminal Terminal => _terminalView.Terminal;

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

	public int ViewportY
	{
		get
		{
			return _terminalView?.ViewportY ?? 0;
		}
		set
		{
			if (_terminalView != null)
			{
				_terminalView.ViewportY = value;
			}
		}
	}

	public int MaxScrollback => _terminalView?.MaxScrollback ?? 0;

	public int ViewportLines => _terminalView?.ViewportLines ?? 0;

	public bool IsAlternateBuffer => _terminalView?.IsAlternateBuffer ?? false;

	public bool IsLive => _terminalView?.IsLive ?? false;

	public long SessionId => _terminalView?.SessionId ?? 0;

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

	public double CharWidth => _terminalView?.CharWidth ?? 0.0;

	public double CharHeight => _terminalView?.CharHeight ?? 0.0;

	public string CurrentLineText => _terminalView?.CurrentLineText ?? string.Empty;

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

	public IReadOnlyList<TerminalView.VisibleMark> VisibleMarks => _terminalView?.VisibleMarks ?? Array.Empty<TerminalView.VisibleMark>();

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

	public int SearchHitCount => _terminalView?.SearchHitCount ?? 0;

	public int SearchCurrentIndex => _terminalView?.SearchCurrentIndex ?? (-1);

	public bool SearchTruncated => _terminalView?.SearchTruncated ?? false;

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

	public bool IsFollowingTail => _terminalView?.IsFollowingTail ?? true;

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

	public int ExitCode => _terminalView.ExitCode;

	public int Pid => _terminalView.Pid;

	public event EventHandler? ShellReady;

	public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

	public event EventHandler<TerminalMessageEventArgs>? WritingMessage;

	public event EventHandler<OutputReceivedEventArgs>? OutputReceived;

	public event EventHandler<UrlClickedEventArgs>? UrlClicked;

	public event EventHandler<TerminalNotificationEventArgs> NotificationRequested
	{
		add
		{
			AddHandler(TerminalView.NotificationRequestedEvent, value);
		}
		remove
		{
			RemoveHandler(TerminalView.NotificationRequestedEvent, value);
		}
	}

	public event EventHandler<TerminalAttentionEventArgs> AttentionRequested
	{
		add
		{
			AddHandler(TerminalView.AttentionRequestedEvent, value);
		}
		remove
		{
			RemoveHandler(TerminalView.AttentionRequestedEvent, value);
		}
	}

	public event EventHandler<string>? InputSent;

	static TerminalControl()
	{
		TextDecorationsProperty = AvaloniaProperty.Register<TerminalControl, TextDecorationLocation?>("TextDecorations");
		SelectionBrushProperty = AvaloniaProperty.Register<TerminalControl, IBrush>("SelectionBrush", new SolidColorBrush(Color.FromArgb(128, 0, 120, 215)));
		ProcessProperty = AvaloniaProperty.Register<TerminalControl, string>("Process", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash");
		ProcessArgsProperty = AvaloniaProperty.Register<TerminalControl, IList<string>>("ProcessArgs", Array.Empty<string>());
		StartingDirectoryProperty = AvaloniaProperty.Register<TerminalControl, string>("StartingDirectory", Environment.CurrentDirectory);
		CurrentDirectoryProperty = AvaloniaProperty.RegisterDirect("CurrentDirectory", (TerminalControl o) => o.CurrentDirectory);
		BufferSizeProperty = AvaloniaProperty.Register<TerminalControl, int>("BufferSize", 1000);
		OptionsProperty = AvaloniaProperty.Register<TerminalControl, TerminalOptions>("Options");
		ShowCaretOnClickProperty = AvaloniaProperty.Register<TerminalControl, bool>("ShowCaretOnClick", defaultValue: false);
		AutoCopyOnSelectionProperty = AvaloniaProperty.Register<TerminalControl, bool>("AutoCopyOnSelection", defaultValue: true);
		VerbatimCommandLineProperty = AvaloniaProperty.Register<TerminalControl, bool>("VerbatimCommandLine", defaultValue: false);
		EnvironmentVariablesProperty = AvaloniaProperty.Register<TerminalControl, IDictionary<string, string>>("EnvironmentVariables");
		LigaturesProperty = AvaloniaProperty.Register<TerminalControl, bool>("Ligatures", defaultValue: false);
		BoldFontProperty = AvaloniaProperty.Register<TerminalControl, bool>("BoldFont", defaultValue: true);
		ConvertEolProperty = AvaloniaProperty.Register<TerminalControl, bool>("ConvertEol", defaultValue: false);
		AllowWindowOpsProperty = AvaloniaProperty.Register<TerminalControl, bool>("AllowWindowOps", defaultValue: false);
		UseSkiaRendererProperty = AvaloniaProperty.Register<TerminalControl, bool>("UseSkiaRenderer", defaultValue: false);
		CursorColorProperty = AvaloniaProperty.Register<TerminalControl, Color>("CursorColor", Colors.White);
		CursorStyleProperty = AvaloniaProperty.Register<TerminalControl, CursorStyle>("CursorStyle", CursorStyle.Bar);
		CursorBlinkProperty = AvaloniaProperty.Register<TerminalControl, bool>("CursorBlink", defaultValue: true);
		CursorBlinkRateProperty = AvaloniaProperty.Register<TerminalControl, int>("CursorBlinkRate", 530);
		_stylesLoaded = false;
		ShortcutModeProperty = AvaloniaProperty.Register<TerminalControl, ShortcutMode>("ShortcutMode", ShortcutMode.Terminal);
		SuppressCursorProperty = AvaloniaProperty.Register<TerminalControl, bool>("SuppressCursor", defaultValue: false);
		GutterWidthProperty = AvaloniaProperty.Register<TerminalControl, double>("GutterWidth", 0.0);
		GutterPromptBrushProperty = AvaloniaProperty.Register<TerminalControl, IBrush>("GutterPromptBrush");
		GutterSuccessBrushProperty = AvaloniaProperty.Register<TerminalControl, IBrush>("GutterSuccessBrush");
		GutterFailureBrushProperty = AvaloniaProperty.Register<TerminalControl, IBrush>("GutterFailureBrush");
		SearchHighlightBrushProperty = AvaloniaProperty.Register<TerminalControl, IBrush>("SearchHighlightBrush", new SolidColorBrush(Color.FromArgb(90, 240, 180, 41)));
		SearchCurrentBrushProperty = AvaloniaProperty.Register<TerminalControl, IBrush>("SearchCurrentBrush", new SolidColorBrush(Color.FromArgb(160, 240, 180, 41)));
		OutputReceivedOnReadTaskProperty = AvaloniaProperty.Register<TerminalControl, bool>("OutputReceivedOnReadTask", defaultValue: false);
		AutoScrollToBottomProperty = AvaloniaProperty.Register<TerminalControl, bool>("AutoScrollToBottom", defaultValue: true);
		LoadDefaultStyles();
		InputElement.FocusableProperty.OverrideDefaultValue<TerminalControl>(defaultValue: true);
		TemplatedControl.FontFamilyProperty.OverrideDefaultValue<TerminalControl>(TerminalView.DefaultFontFamily);
	}

	private static void LoadDefaultStyles()
	{
		if (_stylesLoaded || Application.Current == null)
		{
			return;
		}
		Uri uri = new Uri("avares://Iciclecreek.Avalonia.Terminal/Themes/Generic.axaml");
		foreach (IStyle style in Application.Current.Styles)
		{
			if (style is TerminalGenericStyles || (style is StyleInclude styleInclude && styleInclude.Source == uri))
			{
				_stylesLoaded = true;
				return;
			}
		}
		IStyle item = new TerminalGenericStyles();
		Application.Current.Styles.Insert(0, item);
		_stylesLoaded = true;
	}

	public TerminalControl()
	{
		LoadDefaultStyles();
	}

	public void Dispose()
	{
		_terminalView?.Dispose();
		GC.SuppressFinalize(this);
	}

	private void OnTerminalViewInputSent(object? sender, string data)
	{
		InputSent?.Invoke(this, data);
	}

	public void WaitForExit(int ms)
	{
		_terminalView.WaitForExit(ms);
	}

	public void Kill()
	{
		_terminalView.Kill();
	}

	public Task SendInputAsync(string text, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _terminalView?.SendInputAsync(text, cancellationToken) ?? Task.CompletedTask;
	}

	public Task<bool> CopyAsync()
	{
		return _terminalView?.CopyAsync() ?? Task.FromResult(result: false);
	}

	public Task PasteAsync()
	{
		return _terminalView?.PasteAsync() ?? Task.CompletedTask;
	}

	public void AttachConnection(IPtyConnection connection)
	{
		if (_terminalView == null)
		{
			ApplyTemplate();
		}
		if (_terminalView == null)
		{
			throw new InvalidOperationException("TerminalControl template has not been applied yet.");
		}
		_terminalView.AttachConnection(connection);
	}

	public IPtyConnection? DetachConnection()
	{
		return _terminalView?.DetachConnection();
	}

	public void BeginReparent()
	{
		_terminalView?.BeginReparent();
	}

	public void EndReparent()
	{
		_terminalView?.EndReparent();
	}

	public Task<bool> CutAsync()
	{
		return _terminalView?.CutAsync() ?? Task.FromResult(result: false);
	}

	public void ClearScreen()
	{
		_terminalView?.ClearScreen();
	}

	public bool ScrollToPreviousPrompt()
	{
		return _terminalView?.ScrollToPreviousPrompt() ?? false;
	}

	public bool ScrollToNextPrompt()
	{
		return _terminalView?.ScrollToNextPrompt() ?? false;
	}

	public bool SelectCommandOutput(int bufferRow)
	{
		return _terminalView?.SelectCommandOutput(bufferRow) ?? false;
	}

	public int FindInBuffer(string needle, SearchOptions options = default(SearchOptions))
	{
		return _terminalView?.FindInBuffer(needle, options) ?? 0;
	}

	public bool FindNext()
	{
		return _terminalView?.FindNext() ?? false;
	}

	public bool FindPrevious()
	{
		return _terminalView?.FindPrevious() ?? false;
	}

	public void ClearSearch()
	{
		_terminalView?.ClearSearch();
	}

	public void Refresh()
	{
		_terminalView?.Refresh();
	}

	public void FollowTail()
	{
		_terminalView?.FollowTail();
	}

	public virtual async Task LaunchProcess()
	{
		if (_terminalView == null)
		{
			ApplyTemplate();
		}
		if (_terminalView == null)
		{
			throw new InvalidOperationException("TerminalControl template has not been applied yet.");
		}
		await _terminalView.LaunchProcess();
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (_terminalView != null && !_terminalView.IsFocused)
			{
				_terminalView.Focus();
			}
		}, DispatcherPriority.Input);
	}

	public virtual async Task LaunchProcess(string? startingDirectory, string process, params string[] args)
	{
		StartingDirectory = startingDirectory;
		Process = process;
		ProcessArgs = args ?? Array.Empty<string>();
		await LaunchProcess();
	}

	protected override void OnGotFocus(FocusChangedEventArgs e)
	{
		base.OnGotFocus(e);
		if (_terminalView == null || _terminalView.IsFocused)
		{
			return;
		}
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (_terminalView != null && !_terminalView.IsFocused)
			{
				_terminalView.Focus();
			}
		}, DispatcherPriority.Input);
	}

	protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
	{
		base.OnApplyTemplate(e);
		if (_scrollBar != null)
		{
			_scrollBar.Scroll -= OnScrollBarScroll;
		}
		if (_terminalView != null)
		{
			_terminalView.PropertyChanged -= OnTerminalViewPropertyChanged;
			_terminalView.ProcessExited -= OnTerminalViewProcessExited;
			_terminalView.WritingMessage -= OnTerminalViewWritingMessage;
			_terminalView.ShellReady -= OnTerminalViewShellReady;
			_terminalView.OutputReceived -= OnTerminalViewOutputReceived;
			_terminalView.UrlClicked -= OnTerminalViewUrlClicked;
			_terminalView.InputSent -= OnTerminalViewInputSent;
		}
		SetCurrentDirectory(null);
		_terminalView = e.NameScope.Find<TerminalView>("PART_TerminalView");
		_scrollBar = e.NameScope.Find<ScrollBar>("PART_ScrollBar");
		if (_scrollBar != null && _terminalView != null)
		{
			_scrollBar.Scroll += OnScrollBarScroll;
		}
		if (_terminalView != null)
		{
			_terminalView.Options = Options ?? new TerminalOptions();
			_terminalView.PropertyChanged += OnTerminalViewPropertyChanged;
			_terminalView.ProcessExited += OnTerminalViewProcessExited;
			_terminalView.WritingMessage += OnTerminalViewWritingMessage;
			_terminalView.ShellReady += OnTerminalViewShellReady;
			_terminalView.OutputReceived += OnTerminalViewOutputReceived;
			_terminalView.UrlClicked += OnTerminalViewUrlClicked;
			_terminalView.InputSent += OnTerminalViewInputSent;
			SetCurrentDirectory(_terminalView.CurrentDirectory);
			SetCurrentValue(OptionsProperty, _terminalView.Options);
		}
	}

	private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
	{
		if (_terminalView == null)
		{
			return;
		}
		_applyingScrollBarValue = true;
		try
		{
			_terminalView.ViewportY = (int)Math.Round(e.NewValue);
		}
		finally
		{
			_applyingScrollBarValue = false;
		}
	}

	private void OnTerminalViewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == TerminalView.MaxScrollbackProperty || e.Property == TerminalView.ViewportLinesProperty || e.Property == TerminalView.ViewportYProperty || e.Property == TerminalView.IsAlternateBufferProperty)
		{
			UpdateScrollBar();
		}
		else if (e.Property == TerminalView.CurrentDirectoryProperty)
		{
			SetCurrentDirectory(_terminalView?.CurrentDirectory);
		}
		else if (e.Property == TerminalView.OptionsProperty)
		{
			SetCurrentValue(OptionsProperty, e.NewValue as TerminalOptions);
		}
	}

	private void OnTerminalViewProcessExited(object? sender, ProcessExitedEventArgs e)
	{
		ProcessExited?.Invoke(this, e);
	}

	private void OnTerminalViewWritingMessage(object? sender, TerminalMessageEventArgs e)
	{
		WritingMessage?.Invoke(this, e);
	}

	private void OnTerminalViewShellReady(object? sender, EventArgs e)
	{
		ShellReady?.Invoke(this, e);
	}

	private void OnTerminalViewOutputReceived(object? sender, OutputReceivedEventArgs e)
	{
		OutputReceived?.Invoke(this, e);
	}

	private void OnTerminalViewUrlClicked(object? sender, UrlClickedEventArgs e)
	{
		UrlClicked?.Invoke(this, e);
	}

	private void SetCurrentDirectory(string? currentDirectory)
	{
		SetAndRaise(CurrentDirectoryProperty, ref _currentDirectory, currentDirectory);
	}

	private void UpdateScrollBar()
	{
		if (_scrollBar == null || _terminalView == null)
		{
			return;
		}
		if (_terminalView.IsAlternateBuffer)
		{
			MakeScrollBarInert();
			return;
		}
		int maxScrollback = _terminalView.MaxScrollback;
		int viewportLines = _terminalView.ViewportLines;
		int viewportY = _terminalView.ViewportY;
		if (maxScrollback <= 0)
		{
			MakeScrollBarInert();
			return;
		}
		_scrollBar.Minimum = 0.0;
		_scrollBar.Maximum = maxScrollback;
		_scrollBar.ViewportSize = viewportLines;
		_scrollBar.IsEnabled = true;
		if (!_applyingScrollBarValue)
		{
			_scrollBar.Value = viewportY;
		}
	}

	private void MakeScrollBarInert()
	{
		if (_scrollBar != null)
		{
			_scrollBar.Minimum = 0.0;
			_scrollBar.Maximum = 0.0;
			_scrollBar.Value = 0.0;
			_scrollBar.IsEnabled = false;
		}
	}
}
