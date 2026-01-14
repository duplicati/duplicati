namespace Duplicati.Proprietary.Office365.SourceItems;

/// <summary>
/// The type of the source items found in Office365
/// </summary>
public enum SourceItemType
{
    MetaRoot,
    MetaRootUsers,
    MetaRootGroups,
    MetaRootSites,
    Calendar,
    CalendarGroup,
    CalendarEvent,
    Chat,
    ChatHostedContent,
    ChatMessage,
    ChatMember,
    Drive,
    DriveFile,
    DriveFolder,
    Group,
    GroupCalendar,
    GroupChannel,
    GroupChannelMessage,
    GroupChannelMessageReply,
    GroupConversation,
    GroupConversationThread,
    GroupConversationThreadPost,
    Notebook,
    NotebookSection,
    NotebookSectionGroup,
    Planner,
    Site,
    TaskList,
    TaskListTask,
    TaskListLinkedResource,
    TaskListChecklistItem,
    UserMailbox,
    UserMailboxFolder,
    UserMailboxEmail,
    User,
    UserContact,
    UserPlannerTasks,
    PlannerBucket
}