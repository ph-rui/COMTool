using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Reactive;
using Avalonia.Threading;
using Porta.Pty;
using XTerm;
using XTerm.Common;
using XTerm.Options;
using XTerm.Search;

namespace Iciclecreek.Terminal;

public class TerminalWindow : Window, IDisposable
{
	private TerminalControl? _terminalControl;

	private bool _restoringFocus;

	public static readonly StyledProperty<IBrush> SelectionBrushProperty;

	public static readonly StyledProperty<string> ProcessProperty;

	public static readonly StyledProperty<IList<string>> ProcessArgsProperty;

	public static readonly StyledProperty<string?> StartingDirectoryProperty;

	public static readonly DirectProperty<TerminalWindow, string?> CurrentDirectoryProperty;

	public static readonly StyledProperty<bool> CloseOnProcessExitProperty;

	public static readonly StyledProperty<bool> UpdateTitleFromTerminalProperty;

	public static readonly StyledProperty<TerminalOptions?> OptionsProperty;

	public static readonly StyledProperty<int> BufferSizeProperty;

	public static readonly StyledProperty<bool> ShowCaretOnClickProperty;

	public static readonly StyledProperty<bool> AutoCopyOnSelectionProperty;

	public static readonly StyledProperty<bool> VerbatimCommandLineProperty;

	public static readonly StyledProperty<IDictionary<string, string>?> EnvironmentVariablesProperty;

	public static readonly StyledProperty<TextDecorationLocation?> TextDecorationsProperty;

	public static readonly StyledProperty<bool> LigaturesProperty;

	public static readonly StyledProperty<bool> ConvertEolProperty;

	public static readonly StyledProperty<bool> AllowWindowOpsProperty;

	public static readonly StyledProperty<bool> UseSkiaRendererProperty;

	public static readonly StyledProperty<Color> CursorColorProperty;

	public static readonly StyledProperty<CursorStyle> CursorStyleProperty;

	public static readonly StyledProperty<bool> CursorBlinkProperty;

	public static readonly StyledProperty<int> CursorBlinkRateProperty;

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

	public string? CurrentDirectory => _terminalControl?.CurrentDirectory;

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

	public double CharWidth => _terminalControl?.CharWidth ?? 0.0;

	public double CharHeight => _terminalControl?.CharHeight ?? 0.0;

	public string CurrentLineText => _terminalControl?.CurrentLineText ?? string.Empty;

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

	public IReadOnlyList<TerminalView.VisibleMark> VisibleMarks => _terminalControl?.VisibleMarks ?? Array.Empty<TerminalView.VisibleMark>();

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

	public int SearchHitCount => _terminalControl?.SearchHitCount ?? 0;

	public int SearchCurrentIndex => _terminalControl?.SearchCurrentIndex ?? (-1);

	public bool SearchTruncated => _terminalControl?.SearchTruncated ?? false;

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

	public bool IsFollowingTail => _terminalControl?.IsFollowingTail ?? true;

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

	public XTerm.Terminal Terminal => _terminalControl.Terminal;

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
			return _terminalControl?.ViewportY ?? 0;
		}
		set
		{
			if (_terminalControl != null)
			{
				_terminalControl.ViewportY = value;
			}
		}
	}

	public int MaxScrollback => _terminalControl?.MaxScrollback ?? 0;

	public int ViewportLines => _terminalControl?.ViewportLines ?? 0;

	public bool IsAlternateBuffer => _terminalControl?.IsAlternateBuffer ?? false;

	public bool IsLive => _terminalControl?.IsLive ?? false;

	public long SessionId => _terminalControl?.SessionId ?? 0;

	public int ExitCode => _terminalControl.ExitCode;

	public int Pid => _terminalControl.Pid;

	public bool CloseOnProcessExit
	{
		get
		{
			return GetValue(CloseOnProcessExitProperty);
		}
		set
		{
			SetValue(CloseOnProcessExitProperty, value);
		}
	}

	public bool UpdateTitleFromTerminal
	{
		get
		{
			return GetValue(UpdateTitleFromTerminalProperty);
		}
		set
		{
			SetValue(UpdateTitleFromTerminalProperty, value);
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

	public event EventHandler? ShellReady;

	public event EventHandler<ProcessExitedEventArgs>? ProcessExited;

	public event EventHandler<TerminalMessageEventArgs>? WritingMessage;

	public event EventHandler<OutputReceivedEventArgs>? OutputReceived;

	public event EventHandler<string>? InputSent
	{
		add
		{
			EnsureTerminalControl();
			if (_terminalControl != null)
			{
				_terminalControl.InputSent += value;
			}
		}
		remove
		{
			if (_terminalControl != null)
			{
				_terminalControl.InputSent -= value;
			}
		}
	}

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

	public Task<bool> CutAsync()
	{
		return _terminalControl?.CutAsync() ?? Task.FromResult(result: false);
	}

	public void ClearScreen()
	{
		_terminalControl?.ClearScreen();
	}

	public bool ScrollToPreviousPrompt()
	{
		return _terminalControl?.ScrollToPreviousPrompt() ?? false;
	}

	public bool ScrollToNextPrompt()
	{
		return _terminalControl?.ScrollToNextPrompt() ?? false;
	}

	public bool SelectCommandOutput(int bufferRow)
	{
		return _terminalControl?.SelectCommandOutput(bufferRow) ?? false;
	}

	public int FindInBuffer(string needle, SearchOptions options = default(SearchOptions))
	{
		return _terminalControl?.FindInBuffer(needle, options) ?? 0;
	}

	public bool FindNext()
	{
		return _terminalControl?.FindNext() ?? false;
	}

	public bool FindPrevious()
	{
		return _terminalControl?.FindPrevious() ?? false;
	}

	public void ClearSearch()
	{
		_terminalControl?.ClearSearch();
	}

	public void Refresh()
	{
		_terminalControl?.Refresh();
	}

	public void FollowTail()
	{
		_terminalControl?.FollowTail();
	}

	public void WaitForExit(int ms)
	{
		_terminalControl.WaitForExit(ms);
	}

	public void Kill()
	{
		_terminalControl.Kill();
	}

	public Task SendInputAsync(string text, CancellationToken cancellationToken = default(CancellationToken))
	{
		return _terminalControl?.SendInputAsync(text, cancellationToken) ?? Task.CompletedTask;
	}

	public Task<bool> CopyAsync()
	{
		return _terminalControl?.CopyAsync() ?? Task.FromResult(result: false);
	}

	public Task PasteAsync()
	{
		return _terminalControl?.PasteAsync() ?? Task.CompletedTask;
	}

	public void AttachConnection(IPtyConnection connection)
	{
		EnsureTerminalControl();
		_terminalControl.AttachConnection(connection);
	}

	public IPtyConnection? DetachConnection()
	{
		return _terminalControl?.DetachConnection();
	}

	public void BeginReparent()
	{
		_terminalControl?.BeginReparent();
	}

	public void EndReparent()
	{
		_terminalControl?.EndReparent();
	}

	static TerminalWindow()
	{
		SelectionBrushProperty = AvaloniaProperty.Register<TerminalWindow, IBrush>("SelectionBrush", new SolidColorBrush(Color.FromArgb(128, 0, 120, 215)));
		ProcessProperty = AvaloniaProperty.Register<TerminalWindow, string>("Process", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash");
		ProcessArgsProperty = AvaloniaProperty.Register<TerminalWindow, IList<string>>("ProcessArgs", Array.Empty<string>());
		StartingDirectoryProperty = AvaloniaProperty.Register<TerminalWindow, string>("StartingDirectory", Environment.CurrentDirectory);
		CurrentDirectoryProperty = AvaloniaProperty.RegisterDirect("CurrentDirectory", (TerminalWindow o) => o.CurrentDirectory);
		CloseOnProcessExitProperty = AvaloniaProperty.Register<TerminalWindow, bool>("CloseOnProcessExit", defaultValue: true);
		UpdateTitleFromTerminalProperty = AvaloniaProperty.Register<TerminalWindow, bool>("UpdateTitleFromTerminal", defaultValue: true);
		OptionsProperty = AvaloniaProperty.Register<TerminalWindow, TerminalOptions>("Options");
		BufferSizeProperty = AvaloniaProperty.Register<TerminalWindow, int>("BufferSize", 1000);
		ShowCaretOnClickProperty = AvaloniaProperty.Register<TerminalWindow, bool>("ShowCaretOnClick", defaultValue: false);
		AutoCopyOnSelectionProperty = AvaloniaProperty.Register<TerminalWindow, bool>("AutoCopyOnSelection", defaultValue: true);
		VerbatimCommandLineProperty = AvaloniaProperty.Register<TerminalWindow, bool>("VerbatimCommandLine", defaultValue: false);
		EnvironmentVariablesProperty = AvaloniaProperty.Register<TerminalWindow, IDictionary<string, string>>("EnvironmentVariables");
		TextDecorationsProperty = AvaloniaProperty.Register<TerminalWindow, TextDecorationLocation?>("TextDecorations");
		LigaturesProperty = AvaloniaProperty.Register<TerminalWindow, bool>("Ligatures", defaultValue: false);
		ConvertEolProperty = AvaloniaProperty.Register<TerminalWindow, bool>("ConvertEol", defaultValue: false);
		AllowWindowOpsProperty = AvaloniaProperty.Register<TerminalWindow, bool>("AllowWindowOps", defaultValue: false);
		UseSkiaRendererProperty = AvaloniaProperty.Register<TerminalWindow, bool>("UseSkiaRenderer", defaultValue: false);
		CursorColorProperty = AvaloniaProperty.Register<TerminalWindow, Color>("CursorColor", Colors.White);
		CursorStyleProperty = AvaloniaProperty.Register<TerminalWindow, CursorStyle>("CursorStyle", CursorStyle.Bar);
		CursorBlinkProperty = AvaloniaProperty.Register<TerminalWindow, bool>("CursorBlink", defaultValue: true);
		CursorBlinkRateProperty = AvaloniaProperty.Register<TerminalWindow, int>("CursorBlinkRate", 530);
		ShortcutModeProperty = AvaloniaProperty.Register<TerminalWindow, ShortcutMode>("ShortcutMode", ShortcutMode.Terminal);
		SuppressCursorProperty = AvaloniaProperty.Register<TerminalWindow, bool>("SuppressCursor", defaultValue: false);
		GutterWidthProperty = AvaloniaProperty.Register<TerminalWindow, double>("GutterWidth", 0.0);
		GutterPromptBrushProperty = AvaloniaProperty.Register<TerminalWindow, IBrush>("GutterPromptBrush");
		GutterSuccessBrushProperty = AvaloniaProperty.Register<TerminalWindow, IBrush>("GutterSuccessBrush");
		GutterFailureBrushProperty = AvaloniaProperty.Register<TerminalWindow, IBrush>("GutterFailureBrush");
		SearchHighlightBrushProperty = AvaloniaProperty.Register<TerminalWindow, IBrush>("SearchHighlightBrush", new SolidColorBrush(Color.FromArgb(90, 240, 180, 41)));
		SearchCurrentBrushProperty = AvaloniaProperty.Register<TerminalWindow, IBrush>("SearchCurrentBrush", new SolidColorBrush(Color.FromArgb(160, 240, 180, 41)));
		OutputReceivedOnReadTaskProperty = AvaloniaProperty.Register<TerminalWindow, bool>("OutputReceivedOnReadTask", defaultValue: false);
		AutoScrollToBottomProperty = AvaloniaProperty.Register<TerminalWindow, bool>("AutoScrollToBottom", defaultValue: true);
		TemplatedControl.BackgroundProperty.OverrideDefaultValue<TerminalWindow>(Brushes.Black);
		TemplatedControl.ForegroundProperty.OverrideDefaultValue<TerminalWindow>(Brushes.White);
		TemplatedControl.FontFamilyProperty.OverrideDefaultValue<TerminalWindow>(TerminalView.DefaultFontFamily);
	}

	public TerminalWindow()
	{
		EnsureTerminalControl();
		Opened += OnOpened;
		Activated += OnActivated;
		Deactivated += OnDeactivated;
	}

	private static void EnableWindowCommands(TerminalOptions options)
	{
		WindowOptions windowOptions = options.WindowOptions;
		windowOptions.GetWinPosition = true;
		windowOptions.GetWinSizePixels = true;
		windowOptions.GetWinSizeChars = true;
		windowOptions.GetScreenSizePixels = true;
		windowOptions.GetCellSizePixels = true;
		windowOptions.GetIconTitle = true;
		windowOptions.GetWinTitle = true;
		windowOptions.GetWinState = true;
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

	private TerminalControl EnsureTerminalControl()
	{
		if (_terminalControl != null)
		{
			return _terminalControl;
		}
		_terminalControl = new TerminalControl();
		FontFamily = TerminalView.DefaultFontFamily;
		if (Options == null)
		{
			TerminalOptions terminalOptions = (Options = new TerminalOptions());
		}
		this.GetObservable(OptionsProperty).Subscribe(new AnonymousObserver<TerminalOptions>((TerminalOptions? options) =>
		{
			if (options != null)
			{
				EnableWindowCommands(options);
			}
		}));
		AddHandler(InputElement.PointerPressedEvent, OnAnyPointerPressed, RoutingStrategies.Bubble);
		_terminalControl.ProcessExited += OnTerminalControlProcessExited;
		_terminalControl.WritingMessage += OnTerminalControlWritingMessage;
		_terminalControl.ShellReady += OnTerminalControlShellReady;
		_terminalControl.OutputReceived += OnTerminalControlOutputReceived;
		_terminalControl.UrlClicked += OnTerminalControlUrlClicked;
		TerminalView.AddTitleChangedHandler(_terminalControl, OnTerminalTitleChanged);
		TerminalView.AddWindowMovedHandler(_terminalControl, OnTerminalWindowMoved);
		TerminalView.AddWindowResizedHandler(_terminalControl, OnTerminalWindowResized);
		TerminalView.AddWindowMinimizedHandler(_terminalControl, OnTerminalWindowMinimized);
		TerminalView.AddWindowMaximizedHandler(_terminalControl, OnTerminalWindowMaximized);
		TerminalView.AddWindowRestoredHandler(_terminalControl, OnTerminalWindowRestored);
		TerminalView.AddWindowRaisedHandler(_terminalControl, OnTerminalWindowRaised);
		TerminalView.AddWindowLoweredHandler(_terminalControl, OnTerminalWindowLowered);
		TerminalView.AddWindowFullscreenedHandler(_terminalControl, OnTerminalWindowFullscreened);
		TerminalView.AddBellRangHandler(_terminalControl, OnTerminalBellRang);
		TerminalView.AddWindowInfoRequestedHandler(_terminalControl, OnTerminalWindowInfoRequested);
		_terminalControl.PropertyChanged += OnTerminalControlPropertyChanged;
		_terminalControl.Bind(TemplatedControl.FontFamilyProperty, this.GetObservable(TemplatedControl.FontFamilyProperty));
		_terminalControl.Bind(TemplatedControl.FontSizeProperty, this.GetObservable(TemplatedControl.FontSizeProperty));
		_terminalControl.Bind(TemplatedControl.FontStyleProperty, this.GetObservable(TemplatedControl.FontStyleProperty));
		_terminalControl.Bind(TemplatedControl.FontWeightProperty, this.GetObservable(TemplatedControl.FontWeightProperty));
		_terminalControl.Bind(TemplatedControl.ForegroundProperty, this.GetObservable(TemplatedControl.ForegroundProperty));
		_terminalControl.Bind(TemplatedControl.BackgroundProperty, this.GetObservable(TemplatedControl.BackgroundProperty));
		_terminalControl.Bind(TerminalControl.SelectionBrushProperty, this.GetObservable(SelectionBrushProperty));
		_terminalControl.Bind(TerminalControl.GutterWidthProperty, this.GetObservable(GutterWidthProperty));
		_terminalControl.Bind(TerminalControl.GutterPromptBrushProperty, this.GetObservable(GutterPromptBrushProperty));
		_terminalControl.Bind(TerminalControl.GutterSuccessBrushProperty, this.GetObservable(GutterSuccessBrushProperty));
		_terminalControl.Bind(TerminalControl.GutterFailureBrushProperty, this.GetObservable(GutterFailureBrushProperty));
		_terminalControl.Bind(TerminalControl.SearchHighlightBrushProperty, this.GetObservable(SearchHighlightBrushProperty));
		_terminalControl.Bind(TerminalControl.SearchCurrentBrushProperty, this.GetObservable(SearchCurrentBrushProperty));
		_terminalControl.Bind(TerminalControl.ProcessProperty, this.GetObservable(ProcessProperty));
		_terminalControl.Bind(TerminalControl.StartingDirectoryProperty, this.GetObservable(StartingDirectoryProperty));
		_terminalControl.Bind(TerminalControl.ProcessArgsProperty, this.GetObservable(ProcessArgsProperty));
		_terminalControl.Bind(TerminalControl.OptionsProperty, this.GetObservable(OptionsProperty));
		_terminalControl.Bind(TerminalControl.BufferSizeProperty, this.GetObservable(BufferSizeProperty));
		_terminalControl.Bind(TerminalControl.ShowCaretOnClickProperty, this.GetObservable(ShowCaretOnClickProperty));
		_terminalControl.Bind(TerminalControl.AutoCopyOnSelectionProperty, this.GetObservable(AutoCopyOnSelectionProperty));
		_terminalControl.Bind(TerminalControl.ShortcutModeProperty, this.GetObservable(ShortcutModeProperty));
		_terminalControl.Bind(TerminalControl.SuppressCursorProperty, this.GetObservable(SuppressCursorProperty));
		_terminalControl.Bind(TerminalControl.OutputReceivedOnReadTaskProperty, this.GetObservable(OutputReceivedOnReadTaskProperty));
		_terminalControl.Bind(TerminalControl.AutoScrollToBottomProperty, this.GetObservable(AutoScrollToBottomProperty));
		_terminalControl.Bind(TerminalControl.VerbatimCommandLineProperty, this.GetObservable(VerbatimCommandLineProperty));
		_terminalControl.Bind(TerminalControl.EnvironmentVariablesProperty, this.GetObservable(EnvironmentVariablesProperty));
		_terminalControl.Bind(TerminalControl.TextDecorationsProperty, this.GetObservable(TextDecorationsProperty));
		_terminalControl.Bind(TerminalControl.CursorColorProperty, this.GetObservable(CursorColorProperty));
		_terminalControl.Bind(TerminalControl.LigaturesProperty, this.GetObservable(LigaturesProperty));
		_terminalControl.Bind(TerminalControl.ConvertEolProperty, this.GetObservable(ConvertEolProperty));
		_terminalControl.Bind(TerminalControl.AllowWindowOpsProperty, this.GetObservable(AllowWindowOpsProperty));
		_terminalControl.Bind(TerminalControl.CursorStyleProperty, this.GetObservable(CursorStyleProperty));
		_terminalControl.Bind(TerminalControl.CursorBlinkProperty, this.GetObservable(CursorBlinkProperty));
		_terminalControl.Bind(TerminalControl.CursorBlinkRateProperty, this.GetObservable(CursorBlinkRateProperty));
		Content = _terminalControl;
		if (_terminalControl.Options != null)
		{
			SetCurrentValue(OptionsProperty, _terminalControl.Options);
		}
		return _terminalControl;
	}

	public virtual async Task LaunchProcess()
	{
		await EnsureTerminalControl().LaunchProcess();
		global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
		{
			if (IsVisible)
			{
				Activate();
			}
			RestoreTerminalFocus();
		}, DispatcherPriority.Input);
	}

	public virtual async Task LaunchProcess(string? startingDirectory, string process, params string[] args)
	{
		StartingDirectory = startingDirectory;
		Process = process;
		ProcessArgs = args ?? Array.Empty<string>();
		await LaunchProcess();
	}

	private void OnOpened(object? sender, EventArgs e)
	{
		RestoreTerminalFocus();
	}

	private void OnActivated(object? sender, EventArgs e)
	{
		RestoreTerminalFocus();
	}

	private void OnDeactivated(object? sender, EventArgs e)
	{
	}

	private void RestoreTerminalFocus()
	{
		if (_terminalControl == null || _restoringFocus)
		{
			return;
		}
		_restoringFocus = true;
		try
		{
			if (!IsActive)
			{
				return;
			}
			global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
			{
				if (IsActive && _terminalControl != null && !_terminalControl.IsKeyboardFocusWithin)
				{
					_terminalControl.Focus();
				}
			}, DispatcherPriority.Input);
		}
		finally
		{
			_restoringFocus = false;
		}
	}

	public void Dispose()
	{
		_terminalControl?.Dispose();
		GC.SuppressFinalize(this);
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		Dispose();
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		base.OnUnloaded(e);
		Opened -= OnOpened;
		Activated -= OnActivated;
		Deactivated -= OnDeactivated;
		RemoveHandler(InputElement.PointerPressedEvent, OnAnyPointerPressed);
		if (_terminalControl != null)
		{
			_terminalControl.PropertyChanged -= OnTerminalControlPropertyChanged;
			_terminalControl.ProcessExited -= OnTerminalControlProcessExited;
			_terminalControl.WritingMessage -= OnTerminalControlWritingMessage;
			_terminalControl.ShellReady -= OnTerminalControlShellReady;
			_terminalControl.OutputReceived -= OnTerminalControlOutputReceived;
			_terminalControl.UrlClicked -= OnTerminalControlUrlClicked;
			TerminalView.RemoveTitleChangedHandler(_terminalControl, OnTerminalTitleChanged);
			TerminalView.RemoveWindowMovedHandler(_terminalControl, OnTerminalWindowMoved);
			TerminalView.RemoveWindowResizedHandler(_terminalControl, OnTerminalWindowResized);
			TerminalView.RemoveWindowMinimizedHandler(_terminalControl, OnTerminalWindowMinimized);
			TerminalView.RemoveWindowMaximizedHandler(_terminalControl, OnTerminalWindowMaximized);
			TerminalView.RemoveWindowRestoredHandler(_terminalControl, OnTerminalWindowRestored);
			TerminalView.RemoveWindowRaisedHandler(_terminalControl, OnTerminalWindowRaised);
			TerminalView.RemoveWindowLoweredHandler(_terminalControl, OnTerminalWindowLowered);
			TerminalView.RemoveWindowFullscreenedHandler(_terminalControl, OnTerminalWindowFullscreened);
			TerminalView.RemoveBellRangHandler(_terminalControl, OnTerminalBellRang);
			TerminalView.RemoveWindowInfoRequestedHandler(_terminalControl, OnTerminalWindowInfoRequested);
		}
	}

	private void OnTerminalControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == TerminalControl.CurrentDirectoryProperty)
		{
			RaisePropertyChanged(CurrentDirectoryProperty, e.OldValue as string, e.NewValue as string);
		}
		if (e.Property == TerminalControl.OptionsProperty)
		{
			SetCurrentValue(OptionsProperty, e.NewValue as TerminalOptions);
		}
	}

	private void OnAnyPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		global::Avalonia.Threading.Dispatcher.UIThread.Post(RestoreTerminalFocus, DispatcherPriority.Input);
	}

	private void OnTerminalControlShellReady(object? sender, EventArgs e)
	{
		ShellReady?.Invoke(this, e);
	}

	private void OnTerminalControlOutputReceived(object? sender, OutputReceivedEventArgs e)
	{
		OutputReceived?.Invoke(this, e);
	}

	private void OnTerminalControlUrlClicked(object? sender, UrlClickedEventArgs e)
	{
		UrlClicked?.Invoke(this, e);
	}

	private void OnTerminalControlWritingMessage(object? sender, TerminalMessageEventArgs e)
	{
		WritingMessage?.Invoke(this, e);
	}

	private void OnTerminalControlProcessExited(object? sender, ProcessExitedEventArgs e)
	{
		ProcessExited?.Invoke(this, e);
		if (CloseOnProcessExit)
		{
			Close();
		}
	}

	private void OnTerminalTitleChanged(object? sender, TitleChangedEventArgs e)
	{
		if (UpdateTitleFromTerminal && !e.Handled)
		{
			Title = e.Title;
			e.Handled = true;
		}
	}

	private void OnTerminalWindowMoved(object? sender, WindowMovedEventArgs e)
	{
		if (!e.Handled)
		{
			Position = new PixelPoint(e.X, e.Y);
			e.Handled = true;
		}
	}

	private void OnTerminalWindowResized(object? sender, WindowResizedEventArgs e)
	{
		if (!e.Handled)
		{
			Width = e.Width;
			Height = e.Height;
			e.Handled = true;
		}
	}

	private void OnTerminalWindowMinimized(object? sender, RoutedEventArgs e)
	{
		if (!e.Handled)
		{
			WindowState = WindowState.Minimized;
			e.Handled = true;
		}
	}

	private void OnTerminalWindowMaximized(object? sender, RoutedEventArgs e)
	{
		if (!e.Handled)
		{
			WindowState = WindowState.Maximized;
			e.Handled = true;
		}
	}

	private void OnTerminalWindowRestored(object? sender, RoutedEventArgs e)
	{
		if (!e.Handled)
		{
			WindowState = WindowState.Normal;
			e.Handled = true;
		}
	}

	private void OnTerminalWindowRaised(object? sender, RoutedEventArgs e)
	{
		if (!e.Handled)
		{
			Activate();
			e.Handled = true;
		}
	}

	private void OnTerminalWindowLowered(object? sender, RoutedEventArgs e)
	{
		if (e.Handled)
		{
			return;
		}
		IClassicDesktopStyleApplicationLifetime classicDesktopStyleApplicationLifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
		if (classicDesktopStyleApplicationLifetime != null)
		{
			classicDesktopStyleApplicationLifetime.Windows.FirstOrDefault((Window win) => win != this)?.Activate();
			e.Handled = true;
		}
	}

	private void OnTerminalWindowFullscreened(object? sender, RoutedEventArgs e)
	{
		if (!e.Handled)
		{
			WindowState = WindowState.FullScreen;
			e.Handled = true;
		}
	}

	private void OnTerminalBellRang(object? sender, RoutedEventArgs e)
	{
	}

	private void OnTerminalWindowInfoRequested(object? sender, WindowInfoRequestedEventArgs e)
	{
		if (e.Handled)
		{
			return;
		}
		switch (e.Request)
		{
		case WindowInfoRequest.State:
			e.IsIconified = WindowState == WindowState.Minimized;
			e.Handled = true;
			break;
		case WindowInfoRequest.Position:
			e.X = Position.X;
			e.Y = Position.Y;
			e.Handled = true;
			break;
		case WindowInfoRequest.SizePixels:
			if (_terminalControl != null)
			{
				XTerm.Terminal terminal2 = Terminal;
				if (terminal2 != null)
				{
					e.WidthPixels = terminal2.Cols * terminal2.Options.CellWidthPixels;
					e.HeightPixels = terminal2.Rows * terminal2.Options.CellHeightPixels;
					e.Handled = true;
				}
			}
			break;
		case WindowInfoRequest.ScreenSizePixels:
		{
			Screen screen = Screens.ScreenFromWindow(this);
			if (screen != null)
			{
				e.WidthPixels = screen.Bounds.Width;
				e.HeightPixels = screen.Bounds.Height;
				e.Handled = true;
			}
			break;
		}
		case WindowInfoRequest.CellSizePixels:
			if (_terminalControl != null)
			{
				XTerm.Terminal terminal = Terminal;
				if (terminal != null)
				{
					e.CellWidth = terminal.Options.CellWidthPixels;
					e.CellHeight = terminal.Options.CellHeightPixels;
					e.Handled = true;
				}
			}
			break;
		case WindowInfoRequest.Title:
		case WindowInfoRequest.IconTitle:
			e.Title = Title;
			e.Handled = true;
			break;
		case WindowInfoRequest.SizeCharacters:
			break;
		}
	}
}
