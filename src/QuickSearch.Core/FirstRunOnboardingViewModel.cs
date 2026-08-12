namespace QuickSearch.Core;

public sealed record OnboardingStep(
    string Label,
    string Title,
    string Description,
    string Detail);

public sealed class FirstRunOnboardingViewModel : ObservableObject
{
    private readonly IReadOnlyList<OnboardingStep> _steps;
    private int _currentStepIndex;
    private string _message = string.Empty;

    public FirstRunOnboardingViewModel(string launcherShortcut)
    {
        var shortcut = string.IsNullOrWhiteSpace(launcherShortcut)
            ? "Alt+K"
            : launcherShortcut.Trim();
        _steps =
        [
            new(
                "整理",
                "先建立你的快捷导航",
                "在左侧创建可以无限嵌套的分类，再为常用的真实文件夹建立快捷导航。",
                "快捷导航只保存入口，不会移动或删除磁盘里的真实文件夹。"),
            new(
                "呼出",
                $"按 {shortcut}，随时打开启动器",
                "QuickSearch 会常驻系统托盘。无论正在使用哪个程序，都可以用快捷键呼出搜索框。",
                "快捷键可以在右下角的设置中修改。"),
            new(
                "搜索",
                "输入快捷导航名称，立即定位",
                "启动器会先搜索你保存的快捷导航名称；没有本地结果时，再自动使用 Everything 搜索应用、文件和文件夹。",
                "快捷导航优先，结果更稳定，也更符合你的工作习惯。"),
            new(
                "打开",
                "用键盘完成最后一步",
                "按 Enter 打开选中项，或按 Ctrl+1–9 直接打开对应结果。快捷导航还支持右键编辑、移动和删除。",
                "以后需要帮助时，可以从右下角设置查看当前快捷键。")
        ];

        BackCommand = new RelayCommand(MoveBack, () => !IsFirstStep);
        NextCommand = new RelayCommand(MoveNext);
        SkipCommand = new RelayCommand(() => Skipped?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? Completed;

    public event EventHandler? Skipped;

    public OnboardingStep CurrentStep => _steps[CurrentStepIndex];

    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set
        {
            if (!SetProperty(ref _currentStepIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(IsFirstStep));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(PrimaryActionText));
            BackCommand.NotifyCanExecuteChanged();
        }
    }

    public string ProgressText => $"第 {CurrentStepIndex + 1} 步，共 {_steps.Count} 步";

    public double ProgressValue => (double)(CurrentStepIndex + 1) / _steps.Count;

    public bool IsFirstStep => CurrentStepIndex == 0;

    public bool IsLastStep => CurrentStepIndex == _steps.Count - 1;

    public string PrimaryActionText => IsLastStep ? "开始使用" : "下一步";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value ?? string.Empty);
    }

    public RelayCommand BackCommand { get; }

    public RelayCommand NextCommand { get; }

    public RelayCommand SkipCommand { get; }

    public void ReportSaveFailure(string message) => Message = message;

    private void MoveBack()
    {
        if (!IsFirstStep)
        {
            CurrentStepIndex--;
        }
    }

    private void MoveNext()
    {
        Message = string.Empty;
        if (IsLastStep)
        {
            Completed?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentStepIndex++;
    }
}
