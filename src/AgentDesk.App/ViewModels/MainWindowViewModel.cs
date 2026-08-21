using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AgentDesk.AI;
using AgentDesk.Automation;
using AgentDesk.Core;
using AgentDesk.Infrastructure;
using AgentDesk.Infrastructure.Updates;

namespace AgentDesk.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    public const string QwenSecretKey = "qwen-api-key";

    private readonly SimulationApprovalService _approvalService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly ISecretStore _secretStore;
    private readonly WindowsPlatformAutomation _platformAutomation;
    private readonly FileRunEventStore _runEventStore;
    private readonly FileKnowledgeStore _knowledgeStore;
    private readonly FileProductSizingStore _productSizingStore;
    private readonly FileExperienceMemoryStore _memoryStore;
    private readonly FileAgentSkillStore _skillStore;
    private readonly GitHubUpdateService _updateService;
    private readonly List<RunEvent> _allRunEvents = [];
    private ManualSimulationService _simulationService;
    private AppSettings _settings = AppSettings.Default;
    private AgentRuntimeService? _runtimeService;
    private string _selectedPage = "Overview";
    private string _simulationMessage = "黑色 3XL 还有货吗？";
    private string _statusMessage = "正在载入智能客服配置…";
    private bool _humanReviewed;
    private bool _liveUnlocked;
    private bool _isServiceRunning;
    private bool _modelVerified;
    private bool _hasStoredApiKey;
    private string _modelEndpoint = ModelProviderSettings.Default.Endpoint;
    private string _modelName = ModelProviderSettings.Default.Model;
    private int _modelTimeoutSeconds = ModelProviderSettings.Default.TimeoutSeconds;
    private string _apiKeyInput = string.Empty;
    private string _modelConnectionStatus = "尚未测试";
    private PlatformWindowInfo? _selectedPlatformWindow;
    private string _windowTitleContains = string.Empty;
    private double _inputX = PlatformCalibrationSettings.Default.InputX;
    private double _inputY = PlatformCalibrationSettings.Default.InputY;
    private double _sendX = PlatformCalibrationSettings.Default.SendX;
    private double _sendY = PlatformCalibrationSettings.Default.SendY;
    private int _pollIntervalMilliseconds = PlatformCalibrationSettings.Default.PollIntervalMilliseconds;
    private double _minimumObserverConfidence = PlatformCalibrationSettings.Default.MinimumObserverConfidence;
    private string _calibrationStatus = "尚未发现客服平台窗口";
    private string _knowledgeTitleInput = string.Empty;
    private string _knowledgeContentInput = string.Empty;
    private string _knowledgeAccountScope = "全部账号";
    private string _knowledgeSearch = string.Empty;
    private KnowledgeDisplayItem? _selectedKnowledgeItem;
    private string? _editingKnowledgeId;
    private string _memoryTitleInput = string.Empty;
    private string _memoryContentInput = string.Empty;
    private string _memoryTagsInput = string.Empty;
    private string _memoryAccountScope = "全部账号";
    private string _memoryProductKey = string.Empty;
    private string _memorySearch = string.Empty;
    private string _memoryFilter = "全部";
    private string? _editingMemoryId;
    private bool _autoLearningEnabled = true;
    private int _memoryCandidateLimit = MemoryLearningSettings.Default.CandidateLimit;
    private string _skillNameInput = string.Empty;
    private string _skillDescriptionInput = string.Empty;
    private string _skillCategoryInput = "售前回复";
    private string _skillTriggersInput = string.Empty;
    private string _skillInstructionsInput = string.Empty;
    private string _skillSourceUrlInput = string.Empty;
    private string _skillLicenseInput = "自定义";
    private bool _skillAlwaysApply;
    private string _skillSearch = string.Empty;
    private string? _editingSkillId;
    private string _sizingProductUrlInput = string.Empty;
    private string _sizingProductKeyInput = string.Empty;
    private string _sizingCategoryInput = "裤装";
    private string _sizingFitInput = "西裤";
    private string _sizingVariantInput = "常规版";
    private string _sizingAccountScope = "全部账号";
    private string _sizingMeasurementGuide = "请提供身高、体重；裤装可补充腰围，上衣可补充胸围。";
    private string _sizingSearch = string.Empty;
    private string? _editingSizingId;
    private ProductSizingDisplayItem? _selectedSizingProfile;
    private string _previewHeight = string.Empty;
    private string _previewWeight = string.Empty;
    private string _previewWaist = string.Empty;
    private string _previewBust = string.Empty;
    private string _sizingPreviewResult = "选择一条已保存规则，输入客户数据后试算。";
    private string _logFilter = "全部";
    private AgentExecutionMode _executionMode = AgentExecutionMode.Shadow;
    private int _dailySendLimit = RuntimeSafetySettings.Default.DailySendLimit;
    private int _perMinuteSendLimit = RuntimeSafetySettings.Default.PerMinuteSendLimit;
    private BitmapImage? _calibrationPreview;
    private int _capturedWidth;
    private int _capturedHeight;
    private string _calibrationTarget = "输入框";
    private string _updateStatus = "尚未检查更新";
    private string _updateNotes = "点击“检查更新”获取 GitHub 上的最新正式版本。";
    private int _updateProgress;
    private UpdateRelease? _availableUpdate;

    public MainWindowViewModel(
        SimulationApprovalService approvalService,
        IAppSettingsStore settingsStore,
        ISecretStore secretStore,
        WindowsPlatformAutomation platformAutomation,
        FileRunEventStore runEventStore,
        FileKnowledgeStore knowledgeStore,
        FileProductSizingStore productSizingStore,
        FileExperienceMemoryStore memoryStore,
        FileAgentSkillStore skillStore,
        GitHubUpdateService updateService)
    {
        _approvalService = approvalService;
        _settingsStore = settingsStore;
        _secretStore = secretStore;
        _platformAutomation = platformAutomation;
        _runEventStore = runEventStore;
        _knowledgeStore = knowledgeStore;
        _productSizingStore = productSizingStore;
        _memoryStore = memoryStore;
        _skillStore = skillStore;
        _updateService = updateService;
        _simulationService = CreateRuleSimulationService();

        Accounts =
        [
            new("窗", "同平台多账号", "由 Qwen 识别顶部账号标签", "等待窗口校准", 0)
        ];

        ExecutionModes =
        [
            new(AgentExecutionMode.Shadow, "影子观察（推荐）", "读取、判断和记录，但绝不填入或发送"),
            new(AgentExecutionMode.AutoSend, "低风险自动发送", "仅在全部门禁通过后发送有依据的低风险回复")
        ];
        LogFilters = ["全部", "错误", "已发送", "转人工", "影子结果"];
        MemoryFilters = ["全部", "待审核", "长期记忆", "已停用"];
        SizingCategories = ["裤装", "上衣", "连体/套装", "其他"];
        SizingFits = ["阔腿裤", "直筒裤", "西裤", "短袖", "卫衣", "外套", "其他"];
        SizingVariants = ["常规版", "加长版", "短款", "宽松版", "修身版", "其他"];

        NavigateCommand = new RelayCommand(parameter => SelectedPage = parameter?.ToString() ?? "Overview");
        ToggleServiceCommand = new AsyncRelayCommand(ToggleServiceAsync);
        SendSimulationMessageCommand = new AsyncRelayCommand(
            SendSimulationMessageAsync,
            () => !string.IsNullOrWhiteSpace(SimulationMessage));
        RunRequiredSuiteCommand = new AsyncRelayCommand(RunRequiredSuiteAsync);
        ApproveLiveModeCommand = new AsyncRelayCommand(ApproveLiveModeAsync, () => CanApproveLiveMode);
        SaveModelSettingsCommand = new AsyncRelayCommand(SaveModelSettingsAsync);
        TestModelConnectionCommand = new AsyncRelayCommand(TestModelConnectionAsync);
        DeleteApiKeyCommand = new AsyncRelayCommand(DeleteApiKeyAsync, () => HasStoredApiKey);
        RefreshWindowsCommand = new RelayCommand(_ => RefreshWindows());
        UseSelectedWindowCommand = new RelayCommand(_ => UseSelectedWindow(), _ => SelectedPlatformWindow is not null);
        SaveCalibrationCommand = new AsyncRelayCommand(SaveCalibrationAsync);
        CapturePlatformCommand = new AsyncRelayCommand(CapturePlatformAsync);
        TestInputPointCommand = new AsyncRelayCommand(TestInputPointAsync);
        AddKnowledgeCommand = new AsyncRelayCommand(AddKnowledgeAsync, () => CanAddKnowledge);
        ToggleKnowledgeCommand = new RelayCommand(ToggleKnowledge);
        EditKnowledgeCommand = new RelayCommand(EditKnowledge, parameter => parameter is KnowledgeDisplayItem);
        DeleteKnowledgeCommand = new RelayCommand(DeleteKnowledge, parameter => parameter is KnowledgeDisplayItem);
        CancelKnowledgeEditCommand = new RelayCommand(_ => CancelKnowledgeEdit());
        ImportKnowledgeCommand = new AsyncRelayCommand(ImportKnowledgeAsync);
        ExportKnowledgeCommand = new AsyncRelayCommand(ExportKnowledgeAsync);
        SaveMemoryCommand = new AsyncRelayCommand(SaveMemoryAsync, () => CanSaveMemory);
        ApproveMemoryCommand = new RelayCommand(ApproveMemory, parameter => parameter is MemoryDisplayItem);
        EditMemoryCommand = new RelayCommand(EditMemory, parameter => parameter is MemoryDisplayItem);
        ToggleMemoryCommand = new RelayCommand(ToggleMemory, parameter => parameter is MemoryDisplayItem);
        DeleteMemoryCommand = new RelayCommand(DeleteMemory, parameter => parameter is MemoryDisplayItem);
        CancelMemoryEditCommand = new RelayCommand(_ => CancelMemoryEdit());
        ImportMemoriesCommand = new AsyncRelayCommand(ImportMemoriesAsync);
        ExportMemoriesCommand = new AsyncRelayCommand(ExportMemoriesAsync);
        SaveMemorySettingsCommand = new AsyncRelayCommand(SaveMemorySettingsAsync);
        SaveSkillCommand = new AsyncRelayCommand(SaveSkillAsync, () => CanSaveSkill);
        ApproveSkillCommand = new RelayCommand(ApproveSkill, parameter => parameter is AgentSkillDisplayItem);
        EditSkillCommand = new RelayCommand(EditSkill, parameter => parameter is AgentSkillDisplayItem);
        ToggleSkillCommand = new RelayCommand(ToggleSkill, parameter => parameter is AgentSkillDisplayItem);
        DeleteSkillCommand = new RelayCommand(DeleteSkill, parameter => parameter is AgentSkillDisplayItem);
        CancelSkillEditCommand = new RelayCommand(_ => CancelSkillEdit());
        ImportSkillsCommand = new AsyncRelayCommand(ImportSkillsAsync);
        ExportSkillsCommand = new AsyncRelayCommand(ExportSkillsAsync);
        RestoreRecommendedSkillsCommand = new RelayCommand(_ => RestoreRecommendedSkills());
        SaveSizingProfileCommand = new AsyncRelayCommand(SaveSizingProfileAsync, () => CanSaveSizingProfile);
        AddSizingRowCommand = new RelayCommand(_ => AddSizingRow());
        RemoveSizingRowCommand = new RelayCommand(RemoveSizingRow, parameter => parameter is SizingRowEditor && SizingRows.Count > 1);
        EditSizingProfileCommand = new RelayCommand(EditSizingProfile, parameter => parameter is ProductSizingDisplayItem);
        ToggleSizingProfileCommand = new RelayCommand(ToggleSizingProfile, parameter => parameter is ProductSizingDisplayItem);
        DeleteSizingProfileCommand = new RelayCommand(DeleteSizingProfile, parameter => parameter is ProductSizingDisplayItem);
        CancelSizingEditCommand = new RelayCommand(_ => CancelSizingEdit());
        ImportSizingProfilesCommand = new AsyncRelayCommand(ImportSizingProfilesAsync);
        ExportSizingProfilesCommand = new AsyncRelayCommand(ExportSizingProfilesAsync);
        RunSizingPreviewCommand = new RelayCommand(_ => RunSizingPreview(), _ => SelectedSizingProfile is not null);
        SaveSafetySettingsCommand = new AsyncRelayCommand(SaveSafetySettingsAsync);
        ExportLogsCommand = new AsyncRelayCommand(ExportLogsAsync);
        ClearLogsCommand = new RelayCommand(_ => ClearLogs());
        ResetCalibrationCommand = new RelayCommand(_ => ResetCalibration());
        SelectCalibrationTargetCommand = new RelayCommand(parameter =>
            CalibrationTarget = parameter?.ToString() == "Send" ? "发送按钮" : "输入框");
        StopServiceCommand = new AsyncRelayCommand(StopServiceAsync, () => IsServiceRunning);
        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync);
        DownloadAndInstallUpdateCommand = new AsyncRelayCommand(
            DownloadAndInstallUpdateAsync,
            () => IsUpdateAvailable && !IsServiceRunning);
        SizingRows.Add(new SizingRowEditor());
    }

    public ObservableCollection<AccountDisplayItem> Accounts { get; }
    public ObservableCollection<SimulationTranscriptItem> Transcript { get; } = [];
    public ObservableCollection<SimulationCaseResultItem> SuiteResults { get; } = [];
    public ObservableCollection<RunEventDisplayItem> RunEvents { get; } = [];
    public ObservableCollection<PlatformWindowInfo> PlatformWindows { get; } = [];
    public ObservableCollection<KnowledgeDisplayItem> KnowledgeItems { get; } = [];
    public ObservableCollection<MemoryDisplayItem> MemoryItems { get; } = [];
    public ObservableCollection<AgentSkillDisplayItem> SkillItems { get; } = [];
    public ObservableCollection<ProductSizingDisplayItem> SizingProfiles { get; } = [];
    public ObservableCollection<SizingRowEditor> SizingRows { get; } = [];
    public ObservableCollection<ReadinessDisplayItem> ReadinessItems { get; } = [];
    public IReadOnlyList<ExecutionModeDisplayItem> ExecutionModes { get; }
    public IReadOnlyList<string> LogFilters { get; }
    public IReadOnlyList<string> MemoryFilters { get; }
    public IReadOnlyList<string> SizingCategories { get; }
    public IReadOnlyList<string> SizingFits { get; }
    public IReadOnlyList<string> SizingVariants { get; }

    public event EventHandler? SecretInputCleared;

    public ICommand NavigateCommand { get; }
    public AsyncRelayCommand ToggleServiceCommand { get; }
    public AsyncRelayCommand SendSimulationMessageCommand { get; }
    public AsyncRelayCommand RunRequiredSuiteCommand { get; }
    public AsyncRelayCommand ApproveLiveModeCommand { get; }
    public AsyncRelayCommand SaveModelSettingsCommand { get; }
    public AsyncRelayCommand TestModelConnectionCommand { get; }
    public AsyncRelayCommand DeleteApiKeyCommand { get; }
    public RelayCommand RefreshWindowsCommand { get; }
    public RelayCommand UseSelectedWindowCommand { get; }
    public AsyncRelayCommand SaveCalibrationCommand { get; }
    public AsyncRelayCommand CapturePlatformCommand { get; }
    public AsyncRelayCommand TestInputPointCommand { get; }
    public AsyncRelayCommand AddKnowledgeCommand { get; }
    public RelayCommand ToggleKnowledgeCommand { get; }
    public RelayCommand EditKnowledgeCommand { get; }
    public RelayCommand DeleteKnowledgeCommand { get; }
    public RelayCommand CancelKnowledgeEditCommand { get; }
    public AsyncRelayCommand ImportKnowledgeCommand { get; }
    public AsyncRelayCommand ExportKnowledgeCommand { get; }
    public AsyncRelayCommand SaveMemoryCommand { get; }
    public RelayCommand ApproveMemoryCommand { get; }
    public RelayCommand EditMemoryCommand { get; }
    public RelayCommand ToggleMemoryCommand { get; }
    public RelayCommand DeleteMemoryCommand { get; }
    public RelayCommand CancelMemoryEditCommand { get; }
    public AsyncRelayCommand ImportMemoriesCommand { get; }
    public AsyncRelayCommand ExportMemoriesCommand { get; }
    public AsyncRelayCommand SaveMemorySettingsCommand { get; }
    public AsyncRelayCommand SaveSkillCommand { get; }
    public RelayCommand ApproveSkillCommand { get; }
    public RelayCommand EditSkillCommand { get; }
    public RelayCommand ToggleSkillCommand { get; }
    public RelayCommand DeleteSkillCommand { get; }
    public RelayCommand CancelSkillEditCommand { get; }
    public AsyncRelayCommand ImportSkillsCommand { get; }
    public AsyncRelayCommand ExportSkillsCommand { get; }
    public RelayCommand RestoreRecommendedSkillsCommand { get; }
    public AsyncRelayCommand SaveSizingProfileCommand { get; }
    public RelayCommand AddSizingRowCommand { get; }
    public RelayCommand RemoveSizingRowCommand { get; }
    public RelayCommand EditSizingProfileCommand { get; }
    public RelayCommand ToggleSizingProfileCommand { get; }
    public RelayCommand DeleteSizingProfileCommand { get; }
    public RelayCommand CancelSizingEditCommand { get; }
    public AsyncRelayCommand ImportSizingProfilesCommand { get; }
    public AsyncRelayCommand ExportSizingProfilesCommand { get; }
    public RelayCommand RunSizingPreviewCommand { get; }
    public AsyncRelayCommand SaveSafetySettingsCommand { get; }
    public AsyncRelayCommand ExportLogsCommand { get; }
    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand ResetCalibrationCommand { get; }
    public RelayCommand SelectCalibrationTargetCommand { get; }
    public AsyncRelayCommand StopServiceCommand { get; }
    public AsyncRelayCommand CheckForUpdateCommand { get; }
    public AsyncRelayCommand DownloadAndInstallUpdateCommand { get; }

    public string SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(IsOverviewSelected));
                OnPropertyChanged(nameof(IsAccountsSelected));
                OnPropertyChanged(nameof(IsKnowledgeSelected));
                OnPropertyChanged(nameof(IsMemorySkillsSelected));
                OnPropertyChanged(nameof(IsSizingSelected));
                OnPropertyChanged(nameof(IsRulesSelected));
                OnPropertyChanged(nameof(IsLogsSelected));
                OnPropertyChanged(nameof(IsTestLabSelected));
                OnPropertyChanged(nameof(IsSettingsSelected));
            }
        }
    }

    public bool IsOverviewSelected => SelectedPage == "Overview";
    public bool IsAccountsSelected => SelectedPage == "Accounts";
    public bool IsKnowledgeSelected => SelectedPage == "Knowledge";
    public bool IsMemorySkillsSelected => SelectedPage == "MemorySkills";
    public bool IsSizingSelected => SelectedPage == "Sizing";
    public bool IsRulesSelected => SelectedPage == "Rules";
    public bool IsLogsSelected => SelectedPage == "Logs";
    public bool IsTestLabSelected => SelectedPage == "TestLab";
    public bool IsSettingsSelected => SelectedPage == "Settings";

    public string SimulationMessage
    {
        get => _simulationMessage;
        set
        {
            if (SetProperty(ref _simulationMessage, value))
            {
                SendSimulationMessageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentVersionText => $"当前版本 {CurrentVersion.ToString(3)}";
    public string ApplicationVersionText => $"AI客服 {CurrentVersion.ToString(3)}";

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public string UpdateNotes
    {
        get => _updateNotes;
        private set => SetProperty(ref _updateNotes, value);
    }

    public int UpdateProgress
    {
        get => _updateProgress;
        private set
        {
            if (SetProperty(ref _updateProgress, value))
            {
                OnPropertyChanged(nameof(UpdateActionText));
            }
        }
    }

    public bool IsUpdateAvailable => _availableUpdate is not null;

    public string UpdateActionText => _availableUpdate is null
        ? "下载并安装更新"
        : UpdateProgress is > 0 and < 100
            ? $"正在下载 {UpdateProgress}%"
            : $"下载并安装 v{_availableUpdate.Version.ToString(3)}";

    public bool HumanReviewed
    {
        get => _humanReviewed;
        set
        {
            if (SetProperty(ref _humanReviewed, value))
            {
                RaiseApprovalState();
            }
        }
    }

    public bool LiveUnlocked
    {
        get => _liveUnlocked;
        private set
        {
            if (SetProperty(ref _liveUnlocked, value))
            {
                RaiseServiceState();
            }
        }
    }

    public bool IsServiceRunning
    {
        get => _isServiceRunning;
        private set
        {
            if (SetProperty(ref _isServiceRunning, value))
            {
                RaiseServiceState();
                StopServiceCommand.RaiseCanExecuteChanged();
                DownloadAndInstallUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ModelVerified
    {
        get => _modelVerified;
        private set
        {
            if (SetProperty(ref _modelVerified, value))
            {
                OnPropertyChanged(nameof(ModelReadyText));
                OnPropertyChanged(nameof(ModelSourceText));
                OnPropertyChanged(nameof(ModelVerifiedAtText));
                RaiseApprovalState();
                RaiseServiceState();
            }
        }
    }

    public bool HasStoredApiKey
    {
        get => _hasStoredApiKey;
        private set
        {
            if (SetProperty(ref _hasStoredApiKey, value))
            {
                OnPropertyChanged(nameof(ApiKeyStatusText));
                DeleteApiKeyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ModelEndpoint
    {
        get => _modelEndpoint;
        set => SetProperty(ref _modelEndpoint, value);
    }

    public string ModelName
    {
        get => _modelName;
        set => SetProperty(ref _modelName, value);
    }

    public int ModelTimeoutSeconds
    {
        get => _modelTimeoutSeconds;
        set => SetProperty(ref _modelTimeoutSeconds, value);
    }

    public string ApiKeyInput
    {
        get => _apiKeyInput;
        set => SetProperty(ref _apiKeyInput, value);
    }

    public string ModelConnectionStatus
    {
        get => _modelConnectionStatus;
        private set => SetProperty(ref _modelConnectionStatus, value);
    }

    public PlatformWindowInfo? SelectedPlatformWindow
    {
        get => _selectedPlatformWindow;
        set
        {
            if (SetProperty(ref _selectedPlatformWindow, value))
            {
                UseSelectedWindowCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string WindowTitleContains
    {
        get => _windowTitleContains;
        set
        {
            if (SetProperty(ref _windowTitleContains, value))
            {
                OnPropertyChanged(nameof(CalibrationReady));
                RaiseServiceState();
            }
        }
    }

    public double InputX { get => _inputX; set => SetProperty(ref _inputX, value); }
    public double InputY { get => _inputY; set => SetProperty(ref _inputY, value); }
    public double SendX { get => _sendX; set => SetProperty(ref _sendX, value); }
    public double SendY { get => _sendY; set => SetProperty(ref _sendY, value); }
    public int PollIntervalMilliseconds { get => _pollIntervalMilliseconds; set => SetProperty(ref _pollIntervalMilliseconds, value); }
    public double MinimumObserverConfidence { get => _minimumObserverConfidence; set => SetProperty(ref _minimumObserverConfidence, value); }

    public string CalibrationStatus
    {
        get => _calibrationStatus;
        private set => SetProperty(ref _calibrationStatus, value);
    }

    public string KnowledgeTitleInput
    {
        get => _knowledgeTitleInput;
        set
        {
            if (SetProperty(ref _knowledgeTitleInput, value))
            {
                AddKnowledgeCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanAddKnowledge));
            }
        }
    }

    public string KnowledgeContentInput
    {
        get => _knowledgeContentInput;
        set
        {
            if (SetProperty(ref _knowledgeContentInput, value))
            {
                AddKnowledgeCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanAddKnowledge));
            }
        }
    }

    public string KnowledgeAccountScope
    {
        get => _knowledgeAccountScope;
        set => SetProperty(ref _knowledgeAccountScope, value);
    }

    public string KnowledgeSearch
    {
        get => _knowledgeSearch;
        set
        {
            if (SetProperty(ref _knowledgeSearch, value))
            {
                RefreshKnowledge();
            }
        }
    }

    public KnowledgeDisplayItem? SelectedKnowledgeItem
    {
        get => _selectedKnowledgeItem;
        set => SetProperty(ref _selectedKnowledgeItem, value);
    }

    public string KnowledgeEditorTitle => _editingKnowledgeId is null ? "新增审核知识" : "编辑审核知识";
    public string KnowledgeSaveButtonText => _editingKnowledgeId is null ? "保存为已审核知识" : "保存修改";
    public bool IsEditingKnowledge => _editingKnowledgeId is not null;

    public string MemoryTitleInput
    {
        get => _memoryTitleInput;
        set { if (SetProperty(ref _memoryTitleInput, value)) RaiseMemoryCanExecute(); }
    }

    public string MemoryContentInput
    {
        get => _memoryContentInput;
        set { if (SetProperty(ref _memoryContentInput, value)) RaiseMemoryCanExecute(); }
    }

    public string MemoryTagsInput { get => _memoryTagsInput; set => SetProperty(ref _memoryTagsInput, value); }
    public string MemoryAccountScope { get => _memoryAccountScope; set => SetProperty(ref _memoryAccountScope, value); }
    public string MemoryProductKey { get => _memoryProductKey; set => SetProperty(ref _memoryProductKey, value); }
    public string MemorySearch
    {
        get => _memorySearch;
        set { if (SetProperty(ref _memorySearch, value)) RefreshMemories(); }
    }
    public string MemoryFilter
    {
        get => _memoryFilter;
        set { if (SetProperty(ref _memoryFilter, value)) RefreshMemories(); }
    }
    public bool AutoLearningEnabled { get => _autoLearningEnabled; set => SetProperty(ref _autoLearningEnabled, value); }
    public int MemoryCandidateLimit { get => _memoryCandidateLimit; set => SetProperty(ref _memoryCandidateLimit, value); }
    public string MemoryEditorTitle => _editingMemoryId is null ? "新增记忆候选" : "编辑记忆（将重新审核）";
    public string MemorySaveButtonText => _editingMemoryId is null ? "保存为待审核" : "保存修改并转待审核";
    public bool IsEditingMemory => _editingMemoryId is not null;
    public bool CanSaveMemory => !string.IsNullOrWhiteSpace(MemoryTitleInput)
        && !string.IsNullOrWhiteSpace(MemoryContentInput);

    public string SkillNameInput
    {
        get => _skillNameInput;
        set { if (SetProperty(ref _skillNameInput, value)) RaiseSkillCanExecute(); }
    }
    public string SkillDescriptionInput
    {
        get => _skillDescriptionInput;
        set { if (SetProperty(ref _skillDescriptionInput, value)) RaiseSkillCanExecute(); }
    }
    public string SkillCategoryInput { get => _skillCategoryInput; set => SetProperty(ref _skillCategoryInput, value); }
    public string SkillTriggersInput { get => _skillTriggersInput; set => SetProperty(ref _skillTriggersInput, value); }
    public string SkillInstructionsInput
    {
        get => _skillInstructionsInput;
        set { if (SetProperty(ref _skillInstructionsInput, value)) RaiseSkillCanExecute(); }
    }
    public string SkillSourceUrlInput { get => _skillSourceUrlInput; set => SetProperty(ref _skillSourceUrlInput, value); }
    public string SkillLicenseInput { get => _skillLicenseInput; set => SetProperty(ref _skillLicenseInput, value); }
    public bool SkillAlwaysApply { get => _skillAlwaysApply; set => SetProperty(ref _skillAlwaysApply, value); }
    public string SkillSearch
    {
        get => _skillSearch;
        set { if (SetProperty(ref _skillSearch, value)) RefreshSkills(); }
    }
    public string SkillEditorTitle => _editingSkillId is null ? "新增本地客服技能" : "审核与编辑技能";
    public string SkillSaveButtonText => _editingSkillId is null ? "保存并启用" : "保存、审核并启用";
    public bool IsEditingSkill => _editingSkillId is not null;
    public bool CanSaveSkill => !string.IsNullOrWhiteSpace(SkillNameInput)
        && !string.IsNullOrWhiteSpace(SkillDescriptionInput)
        && !string.IsNullOrWhiteSpace(SkillInstructionsInput);

    public string SizingProductUrlInput
    {
        get => _sizingProductUrlInput;
        set
        {
            if (SetProperty(ref _sizingProductUrlInput, value)) RaiseSizingCanExecute();
        }
    }

    public string SizingProductKeyInput
    {
        get => _sizingProductKeyInput;
        set
        {
            if (SetProperty(ref _sizingProductKeyInput, value)) RaiseSizingCanExecute();
        }
    }

    public string SizingCategoryInput
    {
        get => _sizingCategoryInput;
        set
        {
            if (SetProperty(ref _sizingCategoryInput, value)) RaiseSizingCanExecute();
        }
    }

    public string SizingFitInput
    {
        get => _sizingFitInput;
        set
        {
            if (SetProperty(ref _sizingFitInput, value)) RaiseSizingCanExecute();
        }
    }

    public string SizingVariantInput
    {
        get => _sizingVariantInput;
        set
        {
            if (SetProperty(ref _sizingVariantInput, value)) RaiseSizingCanExecute();
        }
    }

    public string SizingAccountScope
    {
        get => _sizingAccountScope;
        set => SetProperty(ref _sizingAccountScope, value);
    }

    public string SizingMeasurementGuide
    {
        get => _sizingMeasurementGuide;
        set => SetProperty(ref _sizingMeasurementGuide, value);
    }

    public string SizingSearch
    {
        get => _sizingSearch;
        set
        {
            if (SetProperty(ref _sizingSearch, value)) RefreshSizingProfiles();
        }
    }

    public ProductSizingDisplayItem? SelectedSizingProfile
    {
        get => _selectedSizingProfile;
        set
        {
            if (SetProperty(ref _selectedSizingProfile, value))
            {
                RunSizingPreviewCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PreviewHeight { get => _previewHeight; set => SetProperty(ref _previewHeight, value); }
    public string PreviewWeight { get => _previewWeight; set => SetProperty(ref _previewWeight, value); }
    public string PreviewWaist { get => _previewWaist; set => SetProperty(ref _previewWaist, value); }
    public string PreviewBust { get => _previewBust; set => SetProperty(ref _previewBust, value); }
    public string SizingPreviewResult { get => _sizingPreviewResult; private set => SetProperty(ref _sizingPreviewResult, value); }
    public string SizingEditorTitle => _editingSizingId is null ? "新增商品尺码规则" : "编辑商品尺码规则";
    public string SizingSaveButtonText => _editingSizingId is null ? "保存为已审核规则" : "保存修改";
    public bool IsEditingSizing => _editingSizingId is not null;
    public bool CanSaveSizingProfile => !string.IsNullOrWhiteSpace(SizingProductUrlInput)
        && !string.IsNullOrWhiteSpace(SizingProductKeyInput)
        && !string.IsNullOrWhiteSpace(SizingCategoryInput)
        && !string.IsNullOrWhiteSpace(SizingFitInput)
        && !string.IsNullOrWhiteSpace(SizingVariantInput)
        && SizingRows.Count > 0;

    public string LogFilter
    {
        get => _logFilter;
        set
        {
            if (SetProperty(ref _logFilter, value))
            {
                RefreshRunEvents();
            }
        }
    }

    public AgentExecutionMode ExecutionMode
    {
        get => _executionMode;
        set
        {
            if (IsServiceRunning)
            {
                StatusMessage = "运行中不能切换模式，请先停止智能客服";
                return;
            }

            if (value is AgentExecutionMode.AutoSend && !LiveUnlocked)
            {
                StatusMessage = "自动发送仍锁定：请先完成 5 个必测用例并人工批准";
                SelectedPage = "TestLab";
                return;
            }

            if (SetProperty(ref _executionMode, value))
            {
                RaiseServiceState();
                RefreshReadiness();
            }
        }
    }

    public int DailySendLimit
    {
        get => _dailySendLimit;
        set => SetProperty(ref _dailySendLimit, value);
    }

    public int PerMinuteSendLimit
    {
        get => _perMinuteSendLimit;
        set => SetProperty(ref _perMinuteSendLimit, value);
    }

    public BitmapImage? CalibrationPreview
    {
        get => _calibrationPreview;
        private set => SetProperty(ref _calibrationPreview, value);
    }

    public string CalibrationTarget
    {
        get => _calibrationTarget;
        private set => SetProperty(ref _calibrationTarget, value);
    }

    public string CapturedWindowText => _capturedWidth > 0
        ? $"校准基准：{_capturedWidth}×{_capturedHeight}，运行时尺寸漂移超过 12% 将停机"
        : "尚未建立截图尺寸基准";

    public bool CanAddKnowledge => !string.IsNullOrWhiteSpace(KnowledgeTitleInput)
        && !string.IsNullOrWhiteSpace(KnowledgeContentInput);

    public string KnowledgeCountText => $"{KnowledgeItems.Count} 条本地知识";
    public string MemoryCountText => $"{MemoryItems.Count} 条记忆";
    public string SkillCountText => $"{SkillItems.Count} 个技能";
    public string SizingCountText => $"{SizingProfiles.Count} 套尺码规则";
    public string KnowledgeAndSizingCountText => $"{KnowledgeItems.Count} 知识 · {SizingProfiles.Count} 尺码 · {_memoryStore.LoadAll().Count(item => item.ReviewStatus is MemoryReviewStatus.Approved && item.IsEnabled)} 记忆 · {_skillStore.LoadAll().Count(item => item.IsReviewed && item.IsEnabled)} 技能";
    public bool HasKnowledgeItems => KnowledgeItems.Count > 0;
    public bool HasMemoryItems => MemoryItems.Count > 0;
    public bool HasSkillItems => SkillItems.Count > 0;
    public bool HasSizingProfiles => SizingProfiles.Count > 0;
    public bool HasRunEvents => RunEvents.Count > 0;

    public bool CalibrationReady => BuildCalibration().IsValid
        && _capturedWidth > 0
        && _capturedHeight > 0
        && _platformAutomation.FindWindow(WindowTitleContains) is not null;

    public bool CanApproveLiveMode =>
        ModelVerified
        && HumanReviewed
        && _approvalService.CanApprove(SuiteResults.Select(item => item.Source).ToArray());

    public string LiveGateTitle => IsServiceRunning
        ? "正式客服运行中"
        : LiveUnlocked ? "模拟测试已批准" : "正式模式已锁定";

    public string LiveGateDescription => IsServiceRunning
        ? "正在观察已校准客服窗口；停止按钮始终优先，连续故障会自动停机。"
        : LiveUnlocked
            ? "安全门已通过；模型和窗口校准有效时才允许启动正式客服。"
            : "先完成模型连接测试、5 个必测用例和人工检查，才能解锁真实平台接入。";

    public string ServiceButtonText => IsServiceRunning
        ? "停止智能客服"
        : ExecutionMode is AgentExecutionMode.Shadow ? "启动影子观察" : "启动自动发送";

    public string ServiceStatusText => IsServiceRunning
        ? ExecutionMode is AgentExecutionMode.Shadow ? "影子观察运行中" : "低风险自动发送运行中"
        : !ModelVerified ? "等待 Qwen 连接测试"
        : !LiveUnlocked ? "等待人工模拟验收"
        : !CalibrationReady ? "等待真实平台校准"
        : "已就绪，可启动";

    public string ExecutionModeText => ExecutionMode is AgentExecutionMode.Shadow
        ? "影子观察"
        : "低风险自动发送";
    public string ExecutionModeDescription => ExecutionMode is AgentExecutionMode.Shadow
        ? "真实读取和决策，只写运行记录，不填入、不点击发送。适合上线前验证识别准确率。"
        : "仅发送低风险、有可靠依据且通过全部门禁的回复；触发限额或漂移立即停机。";

    public int TodaySentCount => _allRunEvents.Count(item =>
        item.Timestamp.LocalDateTime.Date == DateTime.Today
        && item.Stage is AgentStage.Completed);

    public int TodayHumanCount => _allRunEvents.Count(item =>
        item.Timestamp.LocalDateTime.Date == DateTime.Today
        && item.Stage is AgentStage.HumanRequired);

    public string RecentErrorText => _allRunEvents
        .Where(item => item.IsError)
        .OrderByDescending(item => item.Timestamp)
        .Select(item => item.Summary)
        .FirstOrDefault() ?? "暂无错误";

    public string ModelReadyText => ModelVerified ? "Qwen 已验证" : "Qwen 未验证";
    public string ModelSourceText => ModelVerified ? $"正式与模拟：{ModelName}" : "模拟暂用本地安全规则";
    public string ApiKeyStatusText => HasStoredApiKey ? "已安全保存到 Windows 凭据管理器" : "尚未保存 API Key";
    public string ModelVerifiedAtText => _settings.ModelVerifiedAt is null
        ? "尚无成功验证记录"
        : $"最近验证：{_settings.ModelVerifiedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}";

    private static Version CurrentVersion => NormalizeVersion(
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? typeof(MainWindowViewModel).Assembly.GetName().Version
        ?? new Version(0, 0, 0));

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        ModelEndpoint = _settings.Model.Endpoint;
        ModelName = _settings.Model.Model;
        ModelTimeoutSeconds = _settings.Model.TimeoutSeconds;
        WindowTitleContains = _settings.Platform.WindowTitleContains;
        InputX = _settings.Platform.InputX;
        InputY = _settings.Platform.InputY;
        SendX = _settings.Platform.SendX;
        SendY = _settings.Platform.SendY;
        PollIntervalMilliseconds = _settings.Platform.PollIntervalMilliseconds;
        MinimumObserverConfidence = _settings.Platform.MinimumObserverConfidence;
        _capturedWidth = _settings.Platform.CapturedWidth;
        _capturedHeight = _settings.Platform.CapturedHeight;
        OnPropertyChanged(nameof(CapturedWindowText));
        var safety = _settings.Safety ?? RuntimeSafetySettings.Default;
        var memory = _settings.Memory ?? MemoryLearningSettings.Default;
        DailySendLimit = safety.DailySendLimit;
        PerMinuteSendLimit = safety.PerMinuteSendLimit;
        AutoLearningEnabled = memory.AutoCaptureEnabled;
        MemoryCandidateLimit = memory.CandidateLimit;
        _memoryStore.AutoCaptureEnabled = memory.AutoCaptureEnabled;
        _memoryStore.CandidateLimit = memory.CandidateLimit;
        HasStoredApiKey = !string.IsNullOrWhiteSpace(_secretStore.Read(QwenSecretKey));
        ModelVerified = HasStoredApiKey
            && string.Equals(
                _settings.ModelVerifiedFingerprint,
                BuildModelFingerprint(_settings.Model, _secretStore.Read(QwenSecretKey)!),
                StringComparison.Ordinal);
        ResetSimulationService();
        LiveUnlocked = await _approvalService.IsLiveUnlockedAsync(CurrentSafetyFingerprint());
        _executionMode = safety.ExecutionMode is AgentExecutionMode.AutoSend && !LiveUnlocked
            ? AgentExecutionMode.Shadow
            : safety.ExecutionMode;
        OnPropertyChanged(nameof(ExecutionMode));
        RefreshWindows();
        RefreshKnowledge();
        RefreshMemories();
        RefreshSkills();
        RefreshSizingProfiles();
        foreach (var runEvent in _runEventStore.ReadRecent())
        {
            _allRunEvents.Add(runEvent);
        }
        RefreshRunEvents();
        RefreshReadiness();
        StatusMessage = ModelVerified
            ? "Qwen 已连接；请完成模拟验收与客服平台校准"
            : "请在设置中保存百炼 API Key，并完成 Qwen 连接测试";
        _ = CheckForUpdateQuietlyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_runtimeService is not null)
        {
            await _runtimeService.DisposeAsync();
        }
    }

    private async Task ToggleServiceAsync()
    {
        try
        {
            await ToggleServiceCoreAsync();
        }
        catch (Exception exception)
        {
            IsServiceRunning = false;
            StatusMessage = $"正式客服启动失败：{exception.Message}";
            RecordRunEvent(new RunEvent(DateTimeOffset.Now, "live", AgentStage.Failed, StatusMessage, true));
        }
    }

    private async Task ToggleServiceCoreAsync()
    {
        if (IsServiceRunning)
        {
            if (_runtimeService is not null)
            {
                await _runtimeService.StopAsync();
            }

            IsServiceRunning = false;
            RefreshMemories();
            StatusMessage = "智能客服已停止，所有后续自动化动作已取消";
            return;
        }

        if (!ModelVerified)
        {
            SelectedPage = "Settings";
            StatusMessage = "启动被拒绝：请先保存 API Key 并通过 Qwen 连接测试";
            return;
        }

        if (ExecutionMode is AgentExecutionMode.AutoSend && !LiveUnlocked)
        {
            SelectedPage = "TestLab";
            StatusMessage = "启动被拒绝：请先运行 5 个必测用例并人工批准";
            return;
        }

        var calibration = BuildCalibration();
        if (!calibration.IsValid || _platformAutomation.FindWindow(calibration.WindowTitleContains) is null)
        {
            SelectedPage = "Accounts";
            StatusMessage = "启动被拒绝：客服平台窗口未找到或校准参数无效";
            return;
        }

        var qwen = CreateQwenGenerator(_settings.Model, _secretStore.Read(QwenSecretKey)!);
        var adapter = new LivePlatformAdapter(
            _approvalService,
            CurrentSafetyFingerprint(),
            _platformAutomation,
            qwen,
            calibration,
            ExecutionMode is AgentExecutionMode.AutoSend);
        var orchestrator = new AgentOrchestrator(adapter, qwen, _memoryStore);
        _runtimeService = new AgentRuntimeService(
            orchestrator,
            TimeSpan.FromMilliseconds(calibration.PollIntervalMilliseconds),
            ExecutionMode,
            new RuntimeSafetyLimits(DailySendLimit, PerMinuteSendLimit),
            TodaySentCount);
        _runtimeService.EventRecorded += (_, runEvent) =>
        {
            RecordRunEvent(runEvent);
            if (runEvent.Stage is AgentStage.Stopped)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsServiceRunning = false;
                    StatusMessage = runEvent.Summary;
                });
            }
        };
        await _runtimeService.StartAsync();
        IsServiceRunning = true;
        StatusMessage = ExecutionMode is AgentExecutionMode.Shadow
            ? "影子观察已启动：会识别和生成决策，但绝不会填入或发送"
            : "低风险自动发送已启动；首次截图只建立安全基线，不处理历史会话";
    }

    private async Task StopServiceAsync()
    {
        if (_runtimeService is not null)
        {
            await _runtimeService.StopAsync();
        }

        IsServiceRunning = false;
        RefreshMemories();
        StatusMessage = "智能客服已紧急停止，所有后续自动化动作已取消";
    }

    private async Task SaveSafetySettingsAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var safety = new RuntimeSafetySettings(ExecutionMode, DailySendLimit, PerMinuteSendLimit);
            if (!safety.IsValid)
            {
                throw new InvalidOperationException("每日限额需为 1–10000；每分钟限额需为 1–120。");
            }

            _settings = _settings with { Safety = safety };
            await _settingsStore.SaveAsync(_settings);
            StatusMessage = $"运行策略已保存：{ExecutionModeText}，每日 {DailySendLimit} 条，每分钟 {PerMinuteSendLimit} 条";
            RefreshReadiness();
        }
        catch (Exception exception)
        {
            StatusMessage = $"运行策略保存失败：{exception.Message}";
        }
    }

    private async Task SaveModelSettingsAsync()
    {
        try
        {
            await SaveModelSettingsCoreAsync();
        }
        catch (Exception exception)
        {
            ModelConnectionStatus = $"保存失败：{exception.Message}";
            StatusMessage = ModelConnectionStatus;
        }
    }

    private async Task SaveModelSettingsCoreAsync()
    {
        EnsureServiceStoppedForConfiguration();
        var model = BuildModelSettings();
        ValidateModelSettings(model);
        var oldFingerprint = _settings.ModelVerifiedFingerprint;
        if (!string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            _secretStore.Write(QwenSecretKey, ApiKeyInput.Trim());
            ApiKeyInput = string.Empty;
            SecretInputCleared?.Invoke(this, EventArgs.Empty);
        }

        var secret = _secretStore.Read(QwenSecretKey);
        HasStoredApiKey = !string.IsNullOrWhiteSpace(secret);
        var remainsVerified = HasStoredApiKey
            && string.Equals(oldFingerprint, BuildModelFingerprint(model, secret!), StringComparison.Ordinal);
        _settings = _settings with
        {
            Model = model,
            ModelVerifiedAt = remainsVerified ? _settings.ModelVerifiedAt : null,
            ModelVerifiedFingerprint = remainsVerified ? oldFingerprint : null
        };
        await _settingsStore.SaveAsync(_settings);
        ModelVerified = remainsVerified;
        ResetSimulationService();
        if (!remainsVerified)
        {
            await _approvalService.InvalidateAsync();
            LiveUnlocked = false;
            HumanReviewed = false;
            SuiteResults.Clear();
        }

        ModelConnectionStatus = HasStoredApiKey ? "设置已保存，请测试连接" : "设置已保存，但尚无 API Key";
        StatusMessage = ModelConnectionStatus;
    }

    private async Task TestModelConnectionAsync()
    {
        try
        {
            await SaveModelSettingsCoreAsync();
            var secret = _secretStore.Read(QwenSecretKey);
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException("请先输入百炼 API Key。");
            }

            ModelConnectionStatus = "正在调用 Qwen 进行最小连接测试…";
            StatusMessage = ModelConnectionStatus;
            var model = BuildModelSettings();
            var qwen = CreateQwenGenerator(model, secret);
            var result = await qwen.TestConnectionAsync(CancellationToken.None);
            ModelConnectionStatus = $"{result.Message} · {result.Latency.TotalSeconds:F1} 秒";
            if (!result.Success)
            {
                ModelVerified = false;
                StatusMessage = ModelConnectionStatus;
                return;
            }

            _settings = _settings with
            {
                Model = model,
                ModelVerifiedAt = DateTimeOffset.Now,
                ModelVerifiedFingerprint = BuildModelFingerprint(model, secret)
            };
            await _settingsStore.SaveAsync(_settings);
            ModelVerified = true;
            ResetSimulationService();
            StatusMessage = "Qwen 文本连接成功；模拟测试将开始使用真实大模型";
        }
        catch (Exception exception)
        {
            ModelVerified = false;
            ModelConnectionStatus = $"连接失败：{exception.Message}";
            StatusMessage = ModelConnectionStatus;
        }
    }

    private async Task DeleteApiKeyAsync()
    {
        try
        {
            await DeleteApiKeyCoreAsync();
        }
        catch (Exception exception)
        {
            ModelConnectionStatus = $"删除失败：{exception.Message}";
            StatusMessage = ModelConnectionStatus;
        }
    }

    private async Task DeleteApiKeyCoreAsync()
    {
        EnsureServiceStoppedForConfiguration();
        _secretStore.Delete(QwenSecretKey);
        ApiKeyInput = string.Empty;
        SecretInputCleared?.Invoke(this, EventArgs.Empty);
        HasStoredApiKey = false;
        ModelVerified = false;
        _settings = _settings with { ModelVerifiedAt = null, ModelVerifiedFingerprint = null };
        await _settingsStore.SaveAsync(_settings);
        await _approvalService.InvalidateAsync();
        LiveUnlocked = false;
        ResetSimulationService();
        ModelConnectionStatus = "API Key 已从 Windows 凭据管理器删除";
        StatusMessage = ModelConnectionStatus;
    }

    private async Task CheckForUpdateAsync()
    {
        UpdateStatus = "正在检查 GitHub 最新正式版本…";
        StatusMessage = UpdateStatus;
        try
        {
            var release = await _updateService.CheckForUpdateAsync(CurrentVersion);
            SetAvailableUpdate(release);
            StatusMessage = UpdateStatus;
        }
        catch (Exception exception)
        {
            SetAvailableUpdate(null);
            UpdateStatus = $"检查更新失败：{exception.Message}";
            UpdateNotes = "请确认网络可以访问 GitHub，稍后再试。当前版本仍可正常使用。";
            StatusMessage = UpdateStatus;
        }
    }

    private async Task CheckForUpdateQuietlyAsync()
    {
        try
        {
            var release = await _updateService.CheckForUpdateAsync(CurrentVersion);
            SetAvailableUpdate(release);
        }
        catch
        {
            // Startup update checks never interrupt customer-service initialization.
        }
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        if (_availableUpdate is not { } release)
        {
            return;
        }

        EnsureServiceStoppedForConfiguration();
        var answer = MessageBox.Show(
            $"将下载并安装 AI客服 {release.Version.ToString(3)}。\n\n安装完成后软件会自动重启，本地设置、知识库、记忆体和 API Key 不会被删除。是否继续？",
            "安装软件更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);
        if (answer is not MessageBoxResult.Yes)
        {
            UpdateStatus = "已取消安装，当前版本未改变。";
            return;
        }

        try
        {
            UpdateProgress = 1;
            UpdateStatus = $"正在下载 v{release.Version.ToString(3)} 并校验完整性…";
            StatusMessage = UpdateStatus;
            var progress = new Progress<int>(value => UpdateProgress = value);
            var prepared = await _updateService.PrepareUpdateAsync(release, progress);
            UpdateStatus = "校验通过，正在切换到新版本…";
            StatusMessage = UpdateStatus;

            var targetDirectory = AppContext.BaseDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            var processStartInfo = new ProcessStartInfo
            {
                FileName = prepared.ExecutablePath,
                WorkingDirectory = prepared.PayloadDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processStartInfo.ArgumentList.Add("--apply-update");
            processStartInfo.ArgumentList.Add("--parent-pid");
            processStartInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            processStartInfo.ArgumentList.Add("--source");
            processStartInfo.ArgumentList.Add(prepared.PayloadDirectory);
            processStartInfo.ArgumentList.Add("--target");
            processStartInfo.ArgumentList.Add(targetDirectory);
            processStartInfo.ArgumentList.Add("--executable");
            processStartInfo.ArgumentList.Add("AgentDesk.exe");
            _ = Process.Start(processStartInfo)
                ?? throw new InvalidOperationException("无法启动更新进程。");
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            UpdateProgress = 0;
            UpdateStatus = $"安装更新失败：{exception.Message}";
            StatusMessage = UpdateStatus;
        }
    }

    private void SetAvailableUpdate(UpdateRelease? release)
    {
        _availableUpdate = release;
        UpdateProgress = 0;
        if (release is null)
        {
            UpdateStatus = $"已是最新版本（{CurrentVersion.ToString(3)}）";
            UpdateNotes = "当前没有需要安装的正式更新。";
        }
        else
        {
            UpdateStatus = $"发现新版本 {release.Version.ToString(3)}";
            UpdateNotes = string.IsNullOrWhiteSpace(release.Notes)
                ? "此版本没有填写更新说明。"
                : release.Notes.Length <= 2000 ? release.Notes : release.Notes[..2000] + "…";
        }

        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(UpdateActionText));
        DownloadAndInstallUpdateCommand.RaiseCanExecuteChanged();
    }

    private static Version NormalizeVersion(Version version) => new(
        Math.Max(0, version.Major),
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build));

    private void RefreshWindows()
    {
        PlatformWindows.Clear();
        foreach (var window in _platformAutomation.GetVisibleWindows()
                     .Where(window => !window.Title.Contains("AI客服", StringComparison.OrdinalIgnoreCase)
                                      && !window.Title.Contains("售前智服", StringComparison.OrdinalIgnoreCase)))
        {
            PlatformWindows.Add(window);
        }

        SelectedPlatformWindow = PlatformWindows.FirstOrDefault(window =>
            !string.IsNullOrWhiteSpace(WindowTitleContains)
            && window.Title.Contains(WindowTitleContains, StringComparison.OrdinalIgnoreCase));
        CalibrationStatus = PlatformWindows.Count == 0
            ? "没有发现可用窗口"
            : $"发现 {PlatformWindows.Count} 个可选窗口";
    }

    private void UseSelectedWindow()
    {
        if (IsServiceRunning)
        {
            StatusMessage = "运行中不能更换客服窗口，请先停止智能客服";
            return;
        }

        if (SelectedPlatformWindow is null)
        {
            return;
        }

        WindowTitleContains = SelectedPlatformWindow.Title;
        CalibrationPreview = null;
        _capturedWidth = 0;
        _capturedHeight = 0;
        OnPropertyChanged(nameof(CapturedWindowText));
        CalibrationStatus = $"已选择：{SelectedPlatformWindow.DisplayName}";
    }

    private async Task SaveCalibrationAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var calibration = BuildCalibration();
            if (!calibration.IsValid)
            {
                throw new InvalidOperationException("窗口标题、相对坐标、轮询间隔或置信度无效。");
            }

            var window = _platformAutomation.FindWindow(calibration.WindowTitleContains)
                ?? throw new InvalidOperationException("当前找不到匹配的客服平台窗口。");
            WindowsPlatformAutomation.EnsureStableSize(window, calibration);
            _settings = _settings with { Platform = calibration };
            await _settingsStore.SaveAsync(_settings);
            CalibrationStatus = $"校准已保存：{window.DisplayName}";
            StatusMessage = "平台校准已保存；建议先执行截图测试与输入框定位测试";
            OnPropertyChanged(nameof(CalibrationReady));
            RaiseServiceState();
        }
        catch (Exception exception)
        {
            CalibrationStatus = $"保存失败：{exception.Message}";
            StatusMessage = CalibrationStatus;
        }
    }

    private async Task CapturePlatformAsync()
    {
        await Task.Yield();
        try
        {
            EnsureServiceStoppedForConfiguration();
            var capture = _platformAutomation.Capture(WindowTitleContains);
            _capturedWidth = capture.Window.Width;
            _capturedHeight = capture.Window.Height;
            CalibrationPreview = BuildBitmap(capture.DataUrl);
            OnPropertyChanged(nameof(CapturedWindowText));
            OnPropertyChanged(nameof(CalibrationReady));
            CalibrationStatus = $"截图成功：{capture.Window.Width}×{capture.Window.Height} · 校验 {capture.ContentHash[..10]}";
            StatusMessage = "截图成功；请选择输入框或发送按钮，再点击预览图完成标定";
        }
        catch (Exception exception)
        {
            CalibrationStatus = $"截图失败：{exception.Message}";
            StatusMessage = CalibrationStatus;
        }
    }

    private async Task TestInputPointAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var calibration = BuildCalibration();
            var window = _platformAutomation.FindWindow(calibration.WindowTitleContains)
                ?? throw new InvalidOperationException("未找到客服平台窗口。");
            WindowsPlatformAutomation.EnsureStableSize(window, calibration);
            await _platformAutomation.ClickRelativeAsync(
                window,
                calibration.InputX,
                calibration.InputY,
                CancellationToken.None);
            CalibrationStatus = "输入框定位测试完成：只点击了输入框，没有输入或发送任何内容";
            StatusMessage = CalibrationStatus;
        }
        catch (Exception exception)
        {
            CalibrationStatus = $"定位测试失败：{exception.Message}";
            StatusMessage = CalibrationStatus;
        }
    }

    public void SetCalibrationPoint(double relativeX, double relativeY)
    {
        if (CalibrationPreview is null)
        {
            StatusMessage = "请先测试截图，再在预览图中标定位置";
            return;
        }

        relativeX = Math.Clamp(relativeX, 0, 1);
        relativeY = Math.Clamp(relativeY, 0, 1);
        if (CalibrationTarget == "发送按钮")
        {
            SendX = relativeX;
            SendY = relativeY;
        }
        else
        {
            InputX = relativeX;
            InputY = relativeY;
        }

        CalibrationStatus = $"已标定{CalibrationTarget}：X {relativeX:F3} / Y {relativeY:F3}，请保存校准";
        StatusMessage = CalibrationStatus;
    }

    private void ResetCalibration()
    {
        if (IsServiceRunning)
        {
            StatusMessage = "运行中不能重置校准，请先停止智能客服";
            return;
        }

        InputX = PlatformCalibrationSettings.Default.InputX;
        InputY = PlatformCalibrationSettings.Default.InputY;
        SendX = PlatformCalibrationSettings.Default.SendX;
        SendY = PlatformCalibrationSettings.Default.SendY;
        PollIntervalMilliseconds = PlatformCalibrationSettings.Default.PollIntervalMilliseconds;
        MinimumObserverConfidence = PlatformCalibrationSettings.Default.MinimumObserverConfidence;
        CalibrationStatus = "已恢复安全默认坐标；请重新截图并点击标定后保存";
    }

    private static BitmapImage BuildBitmap(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
        {
            throw new InvalidOperationException("截图数据格式无效。");
        }

        var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private async Task AddKnowledgeAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            if (_editingKnowledgeId is null)
            {
                _knowledgeStore.AddReviewed(
                    KnowledgeTitleInput,
                    KnowledgeContentInput,
                    KnowledgeAccountScope);
            }
            else
            {
                _knowledgeStore.UpdateReviewed(
                    _editingKnowledgeId,
                    KnowledgeTitleInput,
                    KnowledgeContentInput,
                    KnowledgeAccountScope);
            }

            var operation = _editingKnowledgeId is null ? "新增" : "修改";
            CancelKnowledgeEdit();
            RefreshKnowledge();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = $"知识已{operation}并标记为审核通过；正式模式批准已撤销";
        }
        catch (Exception exception)
        {
            StatusMessage = $"知识保存失败：{exception.Message}";
        }
    }

    private void EditKnowledge(object? parameter)
    {
        if (parameter is not KnowledgeDisplayItem item)
        {
            return;
        }

        _editingKnowledgeId = item.Id;
        KnowledgeTitleInput = item.Title;
        KnowledgeContentInput = item.Content;
        KnowledgeAccountScope = item.AccountScope;
        OnPropertyChanged(nameof(KnowledgeEditorTitle));
        OnPropertyChanged(nameof(KnowledgeSaveButtonText));
        OnPropertyChanged(nameof(IsEditingKnowledge));
        StatusMessage = $"正在编辑知识：{item.Title}";
    }

    private async void DeleteKnowledge(object? parameter)
    {
        if (parameter is not KnowledgeDisplayItem item)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"确定删除知识“{item.Title}”吗？此操作会撤销正式模式批准。",
            "删除知识",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer is not MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            EnsureServiceStoppedForConfiguration();
            _knowledgeStore.Delete(item.Id);
            if (_editingKnowledgeId == item.Id)
            {
                CancelKnowledgeEdit();
            }

            RefreshKnowledge();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "知识已删除，正式模式批准已撤销";
        }
        catch (Exception exception)
        {
            StatusMessage = $"删除知识失败：{exception.Message}";
        }
    }

    private void CancelKnowledgeEdit()
    {
        _editingKnowledgeId = null;
        KnowledgeTitleInput = string.Empty;
        KnowledgeContentInput = string.Empty;
        KnowledgeAccountScope = "全部账号";
        OnPropertyChanged(nameof(KnowledgeEditorTitle));
        OnPropertyChanged(nameof(KnowledgeSaveButtonText));
        OnPropertyChanged(nameof(IsEditingKnowledge));
    }

    private async Task ExportKnowledgeAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出审核知识",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"AI客服-知识库-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, _knowledgeStore.ExportJson(), Encoding.UTF8);
        StatusMessage = $"知识已导出：{dialog.FileName}";
    }

    private async Task ImportKnowledgeAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入审核知识",
            Filter = "JSON 文件 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            EnsureServiceStoppedForConfiguration();
            var json = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
            var added = _knowledgeStore.ImportReviewed(json);
            RefreshKnowledge();
            if (added > 0)
            {
                await InvalidateApprovalForKnowledgeChangeAsync();
            }

            StatusMessage = added == 0
                ? "导入完成：没有新增条目（未审核或重复内容已跳过）"
                : $"导入完成：新增 {added} 条审核知识，正式模式批准已撤销";
        }
        catch (Exception exception)
        {
            StatusMessage = $"知识导入失败：{exception.Message}";
        }
    }

    private async void ToggleKnowledge(object? parameter)
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            if (parameter is not KnowledgeDisplayItem item)
            {
                return;
            }

            _knowledgeStore.ToggleEnabled(item.Id);
            RefreshKnowledge();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "知识启用状态已更新；正式模式批准已撤销，请重新运行模拟验收";
        }
        catch (Exception exception)
        {
            StatusMessage = $"知识状态更新失败：{exception.Message}";
        }
    }

    private void RefreshKnowledge()
    {
        KnowledgeItems.Clear();
        var query = KnowledgeSearch.Trim();
        foreach (var item in _knowledgeStore.LoadAll().Where(item =>
                     query.Length == 0
                     || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.AccountScope.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            KnowledgeItems.Add(new KnowledgeDisplayItem(item));
        }

        OnPropertyChanged(nameof(KnowledgeCountText));
        OnPropertyChanged(nameof(KnowledgeAndSizingCountText));
        OnPropertyChanged(nameof(HasKnowledgeItems));
        RefreshReadiness();
    }

    private async Task SaveMemoryAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var wasApproved = _editingMemoryId is not null
                && _memoryStore.LoadAll().Any(item => item.Id == _editingMemoryId
                    && item.ReviewStatus is MemoryReviewStatus.Approved);
            if (_editingMemoryId is null)
            {
                _memoryStore.AddCandidate(MemoryTitleInput, MemoryContentInput, MemoryTagsInput,
                    MemoryAccountScope, MemoryProductKey);
            }
            else
            {
                _memoryStore.UpdateAsCandidate(_editingMemoryId, MemoryTitleInput, MemoryContentInput,
                    MemoryTagsInput, MemoryAccountScope, MemoryProductKey);
            }
            CancelMemoryEdit();
            RefreshMemories();
            if (wasApproved) await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = wasApproved
                ? "记忆修改已转为待审核；旧的正式模式批准已撤销"
                : "记忆候选已保存；审核前不会影响 Qwen 回复";
        }
        catch (Exception exception) { StatusMessage = $"记忆保存失败：{exception.Message}"; }
    }

    private async void ApproveMemory(object? parameter)
    {
        if (parameter is not MemoryDisplayItem item) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            _memoryStore.Approve(item.Id);
            RefreshMemories();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "记忆已人工批准并启用；请重新完成模拟验收";
        }
        catch (Exception exception) { StatusMessage = $"记忆批准失败：{exception.Message}"; }
    }

    private void EditMemory(object? parameter)
    {
        if (parameter is not MemoryDisplayItem item) return;
        var source = item.Source;
        _editingMemoryId = source.Id;
        MemoryTitleInput = source.Title;
        MemoryContentInput = source.Content;
        MemoryTagsInput = source.Tags;
        MemoryAccountScope = source.AccountScope;
        MemoryProductKey = source.ProductKey;
        OnPropertyChanged(nameof(MemoryEditorTitle));
        OnPropertyChanged(nameof(MemorySaveButtonText));
        OnPropertyChanged(nameof(IsEditingMemory));
        StatusMessage = $"正在编辑记忆：{source.Title}";
    }

    private async void ToggleMemory(object? parameter)
    {
        if (parameter is not MemoryDisplayItem item) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            _memoryStore.ToggleEnabled(item.Id);
            RefreshMemories();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "长期记忆启用状态已更新；请重新完成模拟验收";
        }
        catch (Exception exception) { StatusMessage = $"记忆状态更新失败：{exception.Message}"; }
    }

    private async void DeleteMemory(object? parameter)
    {
        if (parameter is not MemoryDisplayItem item) return;
        if (MessageBox.Show($"确定删除记忆“{item.Title}”吗？", "删除记忆",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) is not MessageBoxResult.Yes) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            var affectedReply = item.Source.ReviewStatus is MemoryReviewStatus.Approved && item.Source.IsEnabled;
            _memoryStore.Delete(item.Id);
            if (_editingMemoryId == item.Id) CancelMemoryEdit();
            RefreshMemories();
            if (affectedReply) await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = affectedReply ? "已删除长期记忆；正式模式批准已撤销" : "记忆候选已删除";
        }
        catch (Exception exception) { StatusMessage = $"记忆删除失败：{exception.Message}"; }
    }

    private void CancelMemoryEdit()
    {
        _editingMemoryId = null;
        MemoryTitleInput = MemoryContentInput = MemoryTagsInput = MemoryProductKey = string.Empty;
        MemoryAccountScope = "全部账号";
        OnPropertyChanged(nameof(MemoryEditorTitle));
        OnPropertyChanged(nameof(MemorySaveButtonText));
        OnPropertyChanged(nameof(IsEditingMemory));
    }

    private async Task ImportMemoriesAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入记忆候选", Filter = "JSON 文件 (*.json)|*.json" };
        if (dialog.ShowDialog() is not true) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            var added = _memoryStore.ImportAsCandidates(await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8));
            RefreshMemories();
            StatusMessage = $"导入完成：新增 {added} 条待审核记忆；导入内容不会自动生效";
        }
        catch (Exception exception) { StatusMessage = $"记忆导入失败：{exception.Message}"; }
    }

    private async Task ExportMemoriesAsync()
    {
        var dialog = new SaveFileDialog { Title = "导出记忆体", Filter = "JSON 文件 (*.json)|*.json", FileName = $"AI客服-记忆体-{DateTime.Now:yyyyMMdd}.json" };
        if (dialog.ShowDialog() is not true) return;
        await File.WriteAllTextAsync(dialog.FileName, _memoryStore.ExportJson(), Encoding.UTF8);
        StatusMessage = $"记忆体已导出：{dialog.FileName}";
    }

    private async Task SaveMemorySettingsAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var settings = new MemoryLearningSettings(AutoLearningEnabled, MemoryCandidateLimit);
            if (!settings.IsValid) throw new InvalidOperationException("候选上限需为 50–5000。");
            _settings = _settings with { Memory = settings };
            await _settingsStore.SaveAsync(_settings);
            _memoryStore.AutoCaptureEnabled = settings.AutoCaptureEnabled;
            _memoryStore.CandidateLimit = settings.CandidateLimit;
            StatusMessage = settings.AutoCaptureEnabled
                ? "自动积累已启用；只生成脱敏候选，不会自动影响回复"
                : "自动积累已关闭；已批准的长期记忆仍可使用";
        }
        catch (Exception exception) { StatusMessage = $"记忆设置保存失败：{exception.Message}"; }
    }

    private void RefreshMemories()
    {
        MemoryItems.Clear();
        var query = MemorySearch.Trim();
        IEnumerable<ExperienceMemory> items = _memoryStore.LoadAll();
        items = MemoryFilter switch
        {
            "待审核" => items.Where(item => item.ReviewStatus is MemoryReviewStatus.Candidate),
            "长期记忆" => items.Where(item => item.ReviewStatus is MemoryReviewStatus.Approved),
            "已停用" => items.Where(item => item.ReviewStatus is MemoryReviewStatus.Approved && !item.IsEnabled),
            _ => items
        };
        foreach (var item in items.Where(item => query.Length == 0
                     || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)))
            MemoryItems.Add(new MemoryDisplayItem(item));
        OnPropertyChanged(nameof(MemoryCountText));
        OnPropertyChanged(nameof(HasMemoryItems));
        OnPropertyChanged(nameof(KnowledgeAndSizingCountText));
    }

    private async Task SaveSkillAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            if (_editingSkillId is null)
                _skillStore.AddReviewed(SkillNameInput, SkillDescriptionInput, SkillCategoryInput,
                    SkillTriggersInput, SkillInstructionsInput, SkillSourceUrlInput, SkillLicenseInput, SkillAlwaysApply);
            else
                _skillStore.UpdateReviewed(_editingSkillId, SkillNameInput, SkillDescriptionInput, SkillCategoryInput,
                    SkillTriggersInput, SkillInstructionsInput, SkillSourceUrlInput, SkillLicenseInput, SkillAlwaysApply);
            CancelSkillEdit();
            RefreshSkills();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "客服技能已审核并启用；请重新完成模拟验收";
        }
        catch (Exception exception) { StatusMessage = $"技能保存失败：{exception.Message}"; }
    }

    private async void ApproveSkill(object? parameter)
    {
        if (parameter is not AgentSkillDisplayItem item) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            _skillStore.Approve(item.Id);
            RefreshSkills();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "导入技能已人工审核并启用";
        }
        catch (Exception exception) { StatusMessage = $"技能批准失败：{exception.Message}"; }
    }

    private void EditSkill(object? parameter)
    {
        if (parameter is not AgentSkillDisplayItem item) return;
        var source = item.Source;
        _editingSkillId = source.Id;
        SkillNameInput = source.Name;
        SkillDescriptionInput = source.Description;
        SkillCategoryInput = source.Category;
        SkillTriggersInput = string.Join("，", source.TriggerTerms);
        SkillInstructionsInput = source.Instructions;
        SkillSourceUrlInput = source.SourceUrl;
        SkillLicenseInput = source.License;
        SkillAlwaysApply = source.AlwaysApply;
        OnPropertyChanged(nameof(SkillEditorTitle));
        OnPropertyChanged(nameof(SkillSaveButtonText));
        OnPropertyChanged(nameof(IsEditingSkill));
        StatusMessage = $"正在审核与编辑技能：{source.Name}";
    }

    private async void ToggleSkill(object? parameter)
    {
        if (parameter is not AgentSkillDisplayItem item) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            _skillStore.ToggleEnabled(item.Id);
            RefreshSkills();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "技能启用状态已更新；请重新完成模拟验收";
        }
        catch (Exception exception) { StatusMessage = $"技能状态更新失败：{exception.Message}"; }
    }

    private async void DeleteSkill(object? parameter)
    {
        if (parameter is not AgentSkillDisplayItem item) return;
        if (MessageBox.Show($"确定删除技能“{item.Name}”吗？推荐技能以后可恢复。", "删除技能",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) is not MessageBoxResult.Yes) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            var affectedReply = item.Source.IsReviewed && item.Source.IsEnabled;
            _skillStore.Delete(item.Id);
            if (_editingSkillId == item.Id) CancelSkillEdit();
            RefreshSkills();
            if (affectedReply) await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = affectedReply ? "技能已删除；正式模式批准已撤销" : "待审核技能已删除";
        }
        catch (Exception exception) { StatusMessage = $"技能删除失败：{exception.Message}"; }
    }

    private void CancelSkillEdit()
    {
        _editingSkillId = null;
        SkillNameInput = SkillDescriptionInput = SkillTriggersInput = SkillInstructionsInput = SkillSourceUrlInput = string.Empty;
        SkillCategoryInput = "售前回复";
        SkillLicenseInput = "自定义";
        SkillAlwaysApply = false;
        OnPropertyChanged(nameof(SkillEditorTitle));
        OnPropertyChanged(nameof(SkillSaveButtonText));
        OnPropertyChanged(nameof(IsEditingSkill));
    }

    private async Task ImportSkillsAsync()
    {
        var dialog = new OpenFileDialog { Title = "导入客服技能", Filter = "JSON 文件 (*.json)|*.json" };
        if (dialog.ShowDialog() is not true) return;
        try
        {
            EnsureServiceStoppedForConfiguration();
            var added = _skillStore.ImportForReview(await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8));
            RefreshSkills();
            StatusMessage = $"导入完成：新增 {added} 个待审核技能；审核前不会生效";
        }
        catch (Exception exception) { StatusMessage = $"技能导入失败：{exception.Message}"; }
    }

    private async Task ExportSkillsAsync()
    {
        var dialog = new SaveFileDialog { Title = "导出客服技能", Filter = "JSON 文件 (*.json)|*.json", FileName = $"AI客服-技能-{DateTime.Now:yyyyMMdd}.json" };
        if (dialog.ShowDialog() is not true) return;
        await File.WriteAllTextAsync(dialog.FileName, _skillStore.ExportJson(), Encoding.UTF8);
        StatusMessage = $"客服技能已导出：{dialog.FileName}";
    }

    private async void RestoreRecommendedSkills()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var added = _skillStore.RestoreRecommended();
            RefreshSkills();
            if (added > 0) await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = added == 0 ? "推荐客服技能已齐全" : $"已恢复 {added} 个推荐技能；请重新模拟验收";
        }
        catch (Exception exception) { StatusMessage = $"推荐技能恢复失败：{exception.Message}"; }
    }

    private void RefreshSkills()
    {
        SkillItems.Clear();
        var query = SkillSearch.Trim();
        foreach (var item in _skillStore.LoadAll().Where(item => query.Length == 0
                     || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.TriggerTerms.Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase))))
            SkillItems.Add(new AgentSkillDisplayItem(item));
        OnPropertyChanged(nameof(SkillCountText));
        OnPropertyChanged(nameof(HasSkillItems));
        OnPropertyChanged(nameof(KnowledgeAndSizingCountText));
    }

    private void RaiseMemoryCanExecute()
    {
        OnPropertyChanged(nameof(CanSaveMemory));
        SaveMemoryCommand.RaiseCanExecuteChanged();
    }

    private void RaiseSkillCanExecute()
    {
        OnPropertyChanged(nameof(CanSaveSkill));
        SaveSkillCommand.RaiseCanExecuteChanged();
    }

    private async Task SaveSizingProfileAsync()
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            var rows = SizingRows.Select(row => row.ToModel()).ToArray();
            if (_editingSizingId is null)
            {
                _productSizingStore.AddReviewed(
                    SizingProductUrlInput,
                    SizingProductKeyInput,
                    SizingCategoryInput,
                    SizingFitInput,
                    SizingVariantInput,
                    SizingAccountScope,
                    SizingMeasurementGuide,
                    rows);
            }
            else
            {
                _productSizingStore.UpdateReviewed(
                    _editingSizingId,
                    SizingProductUrlInput,
                    SizingProductKeyInput,
                    SizingCategoryInput,
                    SizingFitInput,
                    SizingVariantInput,
                    SizingAccountScope,
                    SizingMeasurementGuide,
                    rows);
            }

            var operation = _editingSizingId is null ? "新增" : "修改";
            CancelSizingEdit();
            RefreshSizingProfiles();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = $"商品尺码规则已{operation}并审核启用；正式模式批准已撤销";
        }
        catch (Exception exception)
        {
            StatusMessage = $"商品尺码规则保存失败：{exception.Message}";
        }
    }

    private void AddSizingRow()
    {
        SizingRows.Add(new SizingRowEditor());
        RemoveSizingRowCommand.RaiseCanExecuteChanged();
        RaiseSizingCanExecute();
    }

    private void RemoveSizingRow(object? parameter)
    {
        if (parameter is SizingRowEditor row && SizingRows.Count > 1)
        {
            SizingRows.Remove(row);
            RemoveSizingRowCommand.RaiseCanExecuteChanged();
            RaiseSizingCanExecute();
        }
    }

    private void EditSizingProfile(object? parameter)
    {
        if (parameter is not ProductSizingDisplayItem item)
        {
            return;
        }

        var source = item.Source;
        _editingSizingId = source.Id;
        SizingProductUrlInput = source.ProductUrl;
        SizingProductKeyInput = source.ProductKey;
        SizingCategoryInput = source.Category;
        SizingFitInput = source.Fit;
        SizingVariantInput = source.Variant;
        SizingAccountScope = source.AccountScope;
        SizingMeasurementGuide = source.MeasurementGuide;
        SizingRows.Clear();
        foreach (var row in source.Rows)
        {
            SizingRows.Add(new SizingRowEditor(row));
        }

        SelectedSizingProfile = item;
        OnPropertyChanged(nameof(SizingEditorTitle));
        OnPropertyChanged(nameof(SizingSaveButtonText));
        OnPropertyChanged(nameof(IsEditingSizing));
        RemoveSizingRowCommand.RaiseCanExecuteChanged();
        RaiseSizingCanExecute();
        StatusMessage = $"正在编辑：{item.ProductKey} · {item.Classification}";
    }

    private async void ToggleSizingProfile(object? parameter)
    {
        try
        {
            EnsureServiceStoppedForConfiguration();
            if (parameter is not ProductSizingDisplayItem item)
            {
                return;
            }

            _productSizingStore.ToggleEnabled(item.Id);
            RefreshSizingProfiles();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "商品尺码规则启用状态已更新；请重新完成模拟验收";
        }
        catch (Exception exception)
        {
            StatusMessage = $"尺码规则状态更新失败：{exception.Message}";
        }
    }

    private async void DeleteSizingProfile(object? parameter)
    {
        if (parameter is not ProductSizingDisplayItem item)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"确定删除“{item.ProductKey} · {item.Classification}”吗？此操作会撤销正式模式批准。",
            "删除商品尺码规则",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer is not MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            EnsureServiceStoppedForConfiguration();
            _productSizingStore.Delete(item.Id);
            if (_editingSizingId == item.Id)
            {
                CancelSizingEdit();
            }

            RefreshSizingProfiles();
            await InvalidateApprovalForKnowledgeChangeAsync();
            StatusMessage = "商品尺码规则已删除；正式模式批准已撤销";
        }
        catch (Exception exception)
        {
            StatusMessage = $"尺码规则删除失败：{exception.Message}";
        }
    }

    private void CancelSizingEdit()
    {
        _editingSizingId = null;
        SizingProductUrlInput = string.Empty;
        SizingProductKeyInput = string.Empty;
        SizingCategoryInput = "裤装";
        SizingFitInput = "西裤";
        SizingVariantInput = "常规版";
        SizingAccountScope = "全部账号";
        SizingMeasurementGuide = "请提供身高、体重；裤装可补充腰围，上衣可补充胸围。";
        SizingRows.Clear();
        SizingRows.Add(new SizingRowEditor());
        OnPropertyChanged(nameof(SizingEditorTitle));
        OnPropertyChanged(nameof(SizingSaveButtonText));
        OnPropertyChanged(nameof(IsEditingSizing));
        RemoveSizingRowCommand.RaiseCanExecuteChanged();
        RaiseSizingCanExecute();
    }

    private async Task ExportSizingProfilesAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出商品尺码规则",
            Filter = "JSON 文件 (*.json)|*.json",
            FileName = $"AI客服-商品尺码-{DateTime.Now:yyyyMMdd}.json"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, _productSizingStore.ExportJson(), Encoding.UTF8);
        StatusMessage = $"商品尺码规则已导出：{dialog.FileName}";
    }

    private async Task ImportSizingProfilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入商品尺码规则",
            Filter = "JSON 文件 (*.json)|*.json",
            Multiselect = false
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        try
        {
            EnsureServiceStoppedForConfiguration();
            var added = _productSizingStore.ImportReviewed(
                await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8));
            RefreshSizingProfiles();
            if (added > 0)
            {
                await InvalidateApprovalForKnowledgeChangeAsync();
            }

            StatusMessage = added == 0
                ? "导入完成：没有新增规则（无效或重复内容已跳过）"
                : $"导入完成：新增 {added} 套已审核规则；正式模式批准已撤销";
        }
        catch (Exception exception)
        {
            StatusMessage = $"尺码规则导入失败：{exception.Message}";
        }
    }

    private void RefreshSizingProfiles()
    {
        var selectedId = SelectedSizingProfile?.Id;
        SizingProfiles.Clear();
        var query = SizingSearch.Trim();
        foreach (var item in _productSizingStore.LoadAll().Where(item =>
                     query.Length == 0
                     || item.ProductKey.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.ProductUrl.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Fit.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Variant.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.AccountScope.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            SizingProfiles.Add(new ProductSizingDisplayItem(item));
        }

        SelectedSizingProfile = SizingProfiles.FirstOrDefault(item => item.Id == selectedId);
        OnPropertyChanged(nameof(SizingCountText));
        OnPropertyChanged(nameof(KnowledgeAndSizingCountText));
        OnPropertyChanged(nameof(HasSizingProfiles));
        RefreshReadiness();
    }

    private void RunSizingPreview()
    {
        if (SelectedSizingProfile is null)
        {
            SizingPreviewResult = "请先从已保存规则中选择一个商品版本。";
            return;
        }

        try
        {
            var measurements = new CustomerMeasurements(
                ParseOptionalMeasurement(PreviewHeight, "身高"),
                ParseOptionalWeight(PreviewWeight),
                ParseOptionalMeasurement(PreviewWaist, "腰围"),
                ParseOptionalMeasurement(PreviewBust, "胸围"));
            var result = SizingRecommendationEngine.Evaluate(SelectedSizingProfile.Source, measurements);
            SizingPreviewResult = result.Status is SizingMatchStatus.Matched
                ? $"试算通过：{result.Message}"
                : $"不能自动推荐：{result.Message}";
        }
        catch (Exception exception)
        {
            SizingPreviewResult = $"试算输入有误：{exception.Message}";
        }
    }

    private static double? ParseOptionalWeight(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return null;
        }

        var isJin = normalized.EndsWith("斤", StringComparison.Ordinal);
        normalized = normalized.Replace("公斤", string.Empty, StringComparison.Ordinal)
            .Replace("千克", string.Empty, StringComparison.Ordinal)
            .Replace("kg", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("斤", string.Empty, StringComparison.Ordinal)
            .Trim();
        var parsed = ParseNumber(normalized, "体重");
        return isJin ? parsed / 2 : parsed;
    }

    private static double? ParseOptionalMeasurement(string value, string field)
    {
        var normalized = value.Trim()
            .Replace("厘米", string.Empty, StringComparison.Ordinal)
            .Replace("cm", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        return normalized.Length == 0 ? null : ParseNumber(normalized, field);
    }

    private static double ParseNumber(string value, string field)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var result)
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
        {
            throw new InvalidOperationException($"{field}不是有效数字。");
        }

        return result;
    }

    private void RaiseSizingCanExecute()
    {
        OnPropertyChanged(nameof(CanSaveSizingProfile));
        SaveSizingProfileCommand.RaiseCanExecuteChanged();
    }

    private async Task InvalidateApprovalForKnowledgeChangeAsync()
    {
        await _approvalService.InvalidateAsync();
        LiveUnlocked = false;
        HumanReviewed = false;
        SuiteResults.Clear();
    }

    private async Task SendSimulationMessageAsync()
    {
        var text = SimulationMessage.Trim();
        Transcript.Add(new SimulationTranscriptItem("模拟客户", text, "已送入隔离平台", false));
        StatusMessage = ModelVerified
            ? "Qwen 正在生成回复并执行安全检查…"
            : "本地安全规则正在处理模拟消息…";

        try
        {
            var result = await _simulationService.SendManualMessageAsync(text);
            Transcript.Add(result.WasSent
                ? new SimulationTranscriptItem("智能客服", result.Decision.DraftReply, "已自动发送到模拟平台", true)
                : new SimulationTranscriptItem("系统安全门", result.Outcome, "未发送", true));
            StatusMessage = result.Outcome;
        }
        catch (Exception exception)
        {
            StatusMessage = $"模拟处理失败：{exception.Message}";
        }
    }

    private async Task RunRequiredSuiteAsync()
    {
        if (!ModelVerified)
        {
            SelectedPage = "Settings";
            StatusMessage = "请先通过 Qwen 连接测试；正式批准必须基于真实大模型测试结果";
            return;
        }

        StatusMessage = "正在用 Qwen 运行 5 个上线前必测用例…";
        HumanReviewed = false;
        SuiteResults.Clear();
        try
        {
            var results = await _simulationService.RunRequiredSuiteAsync();
            foreach (var result in results)
            {
                SuiteResults.Add(new SimulationCaseResultItem(result));
            }

            RaiseApprovalState();
            var passed = results.Count(result => result.Passed);
            StatusMessage = $"Qwen 必测用例完成：{passed}/{results.Count} 通过，请人工检查结果";
        }
        catch (Exception exception)
        {
            StatusMessage = $"必测用例执行失败：{exception.Message}";
        }
    }

    private async Task ApproveLiveModeAsync()
    {
        try
        {
            if (!ModelVerified)
            {
                throw new InvalidOperationException("Qwen 尚未通过连接测试。");
            }

            await _approvalService.ApproveAsync(
                SuiteResults.Select(item => item.Source).ToArray(),
                HumanReviewed,
                CurrentSafetyFingerprint(),
                Environment.UserName);
            LiveUnlocked = true;
            StatusMessage = "人工批准已保存；完成真实平台截图与输入框校准后即可启动";
        }
        catch (Exception exception)
        {
            StatusMessage = $"无法批准：{exception.Message}";
        }
    }

    private void ResetSimulationService()
    {
        var secret = _secretStore.Read(QwenSecretKey);
        _simulationService = ModelVerified && !string.IsNullOrWhiteSpace(secret)
            ? new ManualSimulationService(CreateQwenGenerator(_settings.Model, secret))
            : CreateRuleSimulationService();
        _simulationService.EventRecorded += (_, runEvent) => RecordRunEvent(runEvent);
    }

    private ManualSimulationService CreateRuleSimulationService()
    {
        var service = new ManualSimulationService(new RuleBasedReplyGenerator());
        service.EventRecorded += (_, runEvent) => RecordRunEvent(runEvent);
        return service;
    }

    private QwenReplyGenerator CreateQwenGenerator(ModelProviderSettings model, string apiKey)
    {
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(model.TimeoutSeconds) };
        return new QwenReplyGenerator(
            httpClient,
            new QwenOptions(
                new Uri(model.Endpoint, UriKind.Absolute),
                apiKey,
                model.Model,
                TimeSpan.FromSeconds(model.TimeoutSeconds)),
            _knowledgeStore,
            _productSizingStore,
            _memoryStore,
            _skillStore);
    }

    private ModelProviderSettings BuildModelSettings() => new(
        ModelEndpoint.Trim(),
        ModelName.Trim(),
        Math.Clamp(ModelTimeoutSeconds, 10, 180),
        true);

    private PlatformCalibrationSettings BuildCalibration() => new(
        WindowTitleContains.Trim(),
        InputX,
        InputY,
        SendX,
        SendY,
        Math.Clamp(PollIntervalMilliseconds, 500, 30000),
        MinimumObserverConfidence,
        _capturedWidth,
        _capturedHeight);

    private static void ValidateModelSettings(ModelProviderSettings model)
    {
        if (!Uri.TryCreate(model.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("模型接口必须是有效的 HTTPS 地址。");
        }


        if (!endpoint.Host.EndsWith(".aliyuncs.com", StringComparison.OrdinalIgnoreCase)
            && !endpoint.Host.Equals("aliyuncs.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("为保护 API Key，Qwen 接口域名必须属于 aliyuncs.com。");
        }

        if (!endpoint.AbsolutePath.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("模型接口地址需要以 /chat/completions 结尾。");
        }

        if (string.IsNullOrWhiteSpace(model.Model))
        {
            throw new InvalidOperationException("模型名称不能为空。");
        }
    }

    private static string BuildModelFingerprint(ModelProviderSettings model, string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{model.Endpoint}|{model.Model}|{secret}")));

    private string CurrentSafetyFingerprint() => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{RuntimeConfiguration.CurrentFingerprint}|{_settings.Model.Endpoint}|{_settings.Model.Model}|{_knowledgeStore.ComputeFingerprint()}|{_productSizingStore.ComputeFingerprint()}|{_memoryStore.ComputeFingerprint()}|{_skillStore.ComputeFingerprint()}")));

    private void RecordRunEvent(RunEvent runEvent)
    {
        try
        {
            _runEventStore.Append(runEvent);
        }
        catch (IOException)
        {
        }

        void Add()
        {
            _allRunEvents.Insert(0, runEvent);
            while (_allRunEvents.Count > 200)
            {
                _allRunEvents.RemoveAt(_allRunEvents.Count - 1);
            }

            RefreshRunEvents();
            OnPropertyChanged(nameof(TodaySentCount));
            OnPropertyChanged(nameof(TodayHumanCount));
            OnPropertyChanged(nameof(RecentErrorText));
        }

        if (Application.Current.Dispatcher.CheckAccess())
        {
            Add();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(Add);
        }
    }

    private void RefreshRunEvents()
    {
        RunEvents.Clear();
        IEnumerable<RunEvent> events = _allRunEvents.OrderByDescending(item => item.Timestamp);
        events = LogFilter switch
        {
            "错误" => events.Where(item => item.IsError),
            "已发送" => events.Where(item => item.Stage is AgentStage.Completed),
            "转人工" => events.Where(item => item.Stage is AgentStage.HumanRequired),
            "影子结果" => events.Where(item => item.Stage is AgentStage.ShadowObserved),
            _ => events
        };

        foreach (var runEvent in events.Take(200))
        {
            RunEvents.Add(new RunEventDisplayItem(runEvent));
        }
        OnPropertyChanged(nameof(HasRunEvents));
    }

    private async Task ExportLogsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出运行记录",
            Filter = "JSONL 文件 (*.jsonl)|*.jsonl",
            FileName = $"AI客服-运行记录-{DateTime.Now:yyyyMMdd-HHmm}.jsonl"
        };
        if (dialog.ShowDialog() is not true)
        {
            return;
        }

        await File.WriteAllTextAsync(dialog.FileName, _runEventStore.ExportJsonLines(), Encoding.UTF8);
        StatusMessage = $"运行记录已导出：{dialog.FileName}";
    }

    private void ClearLogs()
    {
        if (IsServiceRunning)
        {
            StatusMessage = "运行中不能清理记录，请先停止智能客服";
            return;
        }

        var answer = MessageBox.Show(
            "确定清理本机运行记录吗？建议先导出备份。",
            "清理运行记录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer is not MessageBoxResult.Yes)
        {
            return;
        }

        _runEventStore.Clear();
        _allRunEvents.Clear();
        RefreshRunEvents();
        OnPropertyChanged(nameof(TodaySentCount));
        OnPropertyChanged(nameof(TodayHumanCount));
        OnPropertyChanged(nameof(RecentErrorText));
        StatusMessage = "本机运行记录已清理，无法从应用恢复";
    }

    private void RefreshReadiness()
    {
        ReadinessItems.Clear();
        var hasKnowledge = _knowledgeStore.LoadAll().Any(item => item.IsReviewed && item.IsEnabled);
        var hasSizing = _productSizingStore.LoadAll().Any(item => item.IsReviewed && item.IsEnabled);
        var enabledSkills = _skillStore.LoadAll().Count(item => item.IsReviewed && item.IsEnabled);
        var approvedMemories = _memoryStore.LoadAll().Count(item => item.ReviewStatus is MemoryReviewStatus.Approved && item.IsEnabled);
        ReadinessItems.Add(new(
            "01",
            "连接 Qwen",
            ModelVerified ? "模型与密钥验证有效" : "保存 API Key 并完成连接测试",
            ModelVerified ? "已完成" : "待完成",
            ModelVerified,
            "Settings"));
        ReadinessItems.Add(new(
            "02",
            "准备事实与回复策略",
            hasKnowledge || hasSizing
                ? $"知识 {(_knowledgeStore.LoadAll().Count(item => item.IsReviewed && item.IsEnabled))} · 尺码 {(_productSizingStore.LoadAll().Count(item => item.IsReviewed && item.IsEnabled))} · 记忆 {approvedMemories} · 技能 {enabledSkills}"
                : "添加真实知识或商品尺码规则",
            hasKnowledge || hasSizing ? "已完成" : "待完成",
            hasKnowledge || hasSizing,
            hasSizing && !hasKnowledge ? "Sizing" : "Knowledge"));
        ReadinessItems.Add(new(
            "03",
            "模拟验收",
            LiveUnlocked ? "5 个必测用例已人工批准" : "运行必测用例并人工检查",
            LiveUnlocked ? "已完成" : "自动发送必需",
            LiveUnlocked,
            "TestLab"));
        ReadinessItems.Add(new(
            "04",
            "窗口校准",
            CalibrationReady ? CapturedWindowText : "选择窗口、截图并点击标定",
            CalibrationReady ? "已完成" : "待完成",
            CalibrationReady,
            "Accounts"));
        ReadinessItems.Add(new(
            "05",
            "运行策略",
            $"{ExecutionModeText} · 每日 {DailySendLimit} / 每分钟 {PerMinuteSendLimit}",
            "已配置",
            true,
            "Rules"));
    }

    private void RaiseApprovalState()
    {
        OnPropertyChanged(nameof(CanApproveLiveMode));
        ApproveLiveModeCommand.RaiseCanExecuteChanged();
    }

    private void EnsureServiceStoppedForConfiguration()
    {
        if (IsServiceRunning)
        {
            throw new InvalidOperationException("运行中不能修改安全配置，请先停止智能客服。");
        }
    }

    private void RaiseServiceState()
    {
        OnPropertyChanged(nameof(LiveGateTitle));
        OnPropertyChanged(nameof(LiveGateDescription));
        OnPropertyChanged(nameof(ServiceButtonText));
        OnPropertyChanged(nameof(ServiceStatusText));
        OnPropertyChanged(nameof(CalibrationReady));
        OnPropertyChanged(nameof(ExecutionModeText));
        OnPropertyChanged(nameof(ExecutionModeDescription));
        RefreshReadiness();
    }
}
