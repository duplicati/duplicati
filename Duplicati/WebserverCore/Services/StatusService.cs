using Duplicati.Library.RestAPI;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Server;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Abstractions.Notifications;
using Duplicati.WebserverCore.Dto;

namespace Duplicati.WebserverCore.Services;

public class StatusService : IStatusService
{
    private readonly LiveControls m_liveControls;
    private readonly UpdatePollThread m_updatePollThread;
    private readonly IUpdateService m_updateService;
    private readonly IWorkerThreadsManager m_workerThreadsManager;
    private readonly ISettingsService m_settingsService;
    private readonly IScheduler m_scheduler;
    private readonly EventPollNotify m_eventPollNotify;
    private readonly INotificationUpdateService m_notificationUpdateService;

    public StatusService(LiveControls liveControls,
        UpdatePollThread updatePollThread,
        IUpdateService updateService,
        IWorkerThreadsManager workerThreadsManager,
        ISettingsService settingsService,
        IScheduler scheduler,
        EventPollNotify eventPollNotify,
        INotificationUpdateService notificationUpdateService,
        IWebsocketAccessor websocketAccessor)
    {
        m_liveControls = liveControls;
        m_updatePollThread = updatePollThread;
        m_updateService = updateService;
        m_workerThreadsManager = workerThreadsManager;
        m_settingsService = settingsService;
        m_scheduler = scheduler;
        m_eventPollNotify = eventPollNotify;
        m_notificationUpdateService = notificationUpdateService;
    }

    public ServerStatusDto GetStatus()
    {
        var status = new ServerStatusDto
        {
            UpdatedVersion = GetUpdatedVersion(),
            UpdaterState = m_updatePollThread.ThreadState,
            UpdateDownloadProgress = m_updatePollThread.DownloadProgess,
            ActiveTask = m_workerThreadsManager.CurrentTask,
            SchedulerQueueIds = m_scheduler.GetSchedulerQueueIds(),
            LastEventID = m_eventPollNotify.EventNo,
            LastDataUpdateID = m_notificationUpdateService.LastDataUpdateId,
            LastNotificationUpdateID = m_notificationUpdateService.LastNotificationUpdateId
        };
        PullSettings(status);
        PullLiveControls(status);
        return status;
    }

    private void PullLiveControls(ServerStatusDto status)
    {
        status.EstimatedPauseEnd = m_liveControls.EstimatedPauseEnd;
        status.ProgramState = GetProgramState();
        status.SuggestedStatusIcon = MapStateToIcon();
    }

    private SuggestedStatusIcon MapStateToIcon()
    {
        if (m_workerThreadsManager.CurrentTask == null)
        {
            if (m_liveControls.State == LiveControls.LiveControlState.Paused)
                return SuggestedStatusIcon.Paused;

            //TODO: why settings have some errors or warning? those are not settings. Should be moved to some kind of overall system status service 
            var settings = m_settingsService.GetSettings();
            if (settings.UnackedError)
                return SuggestedStatusIcon.ReadyError;

            if (settings.UnackedWarning)
                return SuggestedStatusIcon.ReadyWarning;

            return SuggestedStatusIcon.Ready;
        }

        return m_liveControls.State == LiveControls.LiveControlState.Running
            ? SuggestedStatusIcon.Active
            : SuggestedStatusIcon.ActivePaused;
    }

    private void PullSettings(ServerStatusDto status)
    {
        status.HasError = m_settingsService.GetSettings().UnackedError;
        status.HasWarning = m_settingsService.GetSettings().UnackedWarning;
        status.UpdateDownloadLink = m_settingsService.GetSettings().UpdateCheckNewVersion;
    }

    private string? GetUpdatedVersion()
    {
        return m_updateService.GetUpdateInfo()?.Version;
    }

    private LiveControlState GetProgramState()
    {
        return m_liveControls.State switch
        {
            LiveControls.LiveControlState.Running => LiveControlState.Running,
            LiveControls.LiveControlState.Paused => LiveControlState.Paused,
            _ => throw new ArgumentOutOfRangeException(
                $"Value of {m_liveControls.State} could not be converted to {nameof(LiveControlState)}")
        };
    }
}