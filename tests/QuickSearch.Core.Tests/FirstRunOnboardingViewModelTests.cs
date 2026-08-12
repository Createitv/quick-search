namespace QuickSearch.Core.Tests;

public sealed class FirstRunOnboardingViewModelTests
{
    [Fact]
    public void Steps_ExplainShortcutLocalFirstSearchAndKeyboardOpening()
    {
        var viewModel = new FirstRunOnboardingViewModel("Ctrl+Shift+Space");

        Assert.Contains("快捷导航", viewModel.CurrentStep.Title);
        viewModel.NextCommand.Execute(null);
        Assert.Contains("Ctrl+Shift+Space", viewModel.CurrentStep.Title);
        viewModel.NextCommand.Execute(null);
        Assert.Contains("Everything", viewModel.CurrentStep.Description);
        viewModel.NextCommand.Execute(null);
        Assert.Contains("Ctrl+1–9", viewModel.CurrentStep.Description);
        Assert.Equal("开始使用", viewModel.PrimaryActionText);
    }

    [Fact]
    public void LastStep_CompletesAndBackReturnsToPreviousStep()
    {
        var viewModel = new FirstRunOnboardingViewModel("Alt+K");
        var completed = false;
        viewModel.Completed += (_, _) => completed = true;

        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        viewModel.BackCommand.Execute(null);
        Assert.Equal(2, viewModel.CurrentStepIndex);
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);

        Assert.True(completed);
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void Policy_ShowsOnlyForAnUnfinishedFirstRun(
        bool configurationExisted,
        bool completed,
        bool expected) =>
        Assert.Equal(
            expected,
            FirstRunOnboardingPolicy.ShouldShow(configurationExisted, completed));
}
