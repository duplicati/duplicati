using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Services;

public class StatusService(
    LiveControls liveControls,
    UpdatePollThread updatePollThread,
    IUpdateService updateService,
    IWorkerThreadsManager workerThreadsManager,
    ISettingsService settingsService,
    IScheduler scheduler,
    EventPollNotify eventPollNotify,
    INotificationUpdateService notificationUpdateService)
    : IStatusService
{
    public ServerStatusDto GetStatus()
    {
        var status = new ServerStatusDto
        {
            UpdatedVersion = GetUpdatedVersion(),
            UpdaterState = updatePollThread.ThreadState,
            UpdateDownloadProgress = updatePollThread.DownloadProgess,
            ActiveTask = workerThreadsManager.CurrentTask,
            SchedulerQueueIds = scheduler.GetSchedulerQueueIds(),
            ProposedSchedule = scheduler.GetProposedSchedule(),
            LastEventID = eventPollNotify.EventNo,
            LastDataUpdateID = notificationUpdateService.LastDataUpdateId,
            LastNotificationUpdateID = notificationUpdateService.LastNotificationUpdateId,
            ProgramState = GetProgramState(),
            HasWarning = settingsService.GetSettings().UnackedWarning,
            HasError = settingsService.GetSettings().UnackedError,
            EstimatedPauseEnd = liveControls.EstimatedPauseEnd,
            SuggestedStatusIcon = MapStateToIcon(),
            UpdateDownloadLink = settingsService.GetSettings().NewVersionUpdateUrl
        };
        PullSettings(status);
        PullLiveControls(status);
        return status;
    }

    private void PullLiveControls(ServerStatusDto status)
    {
        status.EstimatedPauseEnd = liveControls.EstimatedPauseEnd;
        status.ProgramState = GetProgramState();
        status.SuggestedStatusIcon = MapStateToIcon();
    }

    private SuggestedStatusIcon MapStateToIcon()
    {
        if (workerThreadsManager.CurrentTask == null)
        {
            if (liveControls.State == LiveControls.LiveControlState.Paused)
                return SuggestedStatusIcon.Paused;

            //TODO: why settings have some errors or warning? those are not settings. Should be moved to some kind of overall system status service 
            var settings = settingsService.GetSettings();
            if (settings.UnackedError)
                return SuggestedStatusIcon.ReadyError;

            if (settings.UnackedWarning)
                return SuggestedStatusIcon.ReadyWarning;

            return SuggestedStatusIcon.Ready;
        }

        return liveControls.State == LiveControls.LiveControlState.Running
            ? SuggestedStatusIcon.Active
            : SuggestedStatusIcon.ActivePaused;
    }

    private void PullSettings(ServerStatusDto status)
    {
        status.HasError = settingsService.GetSettings().UnackedError;
        status.HasWarning = settingsService.GetSettings().UnackedWarning;
        status.UpdateDownloadLink = settingsService.GetSettings().NewVersionUpdateUrl;
    }

    private string? GetUpdatedVersion()
    {
        return updateService.GetUpdateInfo()?.Version;
    }

    private LiveControlState GetProgramState()
    {
        return liveControls.State switch
        {
            LiveControls.LiveControlState.Running => LiveControlState.Running,
            LiveControls.LiveControlState.Paused => LiveControlState.Paused,
            _ => throw new ArgumentOutOfRangeException(
                $"Value of {liveControls.State} could not be converted to {nameof(LiveControlState)}")
        };
    }
}