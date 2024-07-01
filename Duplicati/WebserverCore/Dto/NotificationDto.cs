namespace Duplicati.WebserverCore.Dto
{
    /// <summary>
    /// The notification DTO
    /// </summary>
    public sealed record NotificationDto
    {
        /// <summary>
        /// Gets or sets the ID of the notification.
        /// </summary>
        public required long ID { get; set; }

        /// <summary>
        /// Gets or sets the type of the notification.
        /// </summary>
        public required Duplicati.Server.Serialization.NotificationType Type { get; set; }

        /// <summary>
        /// Gets or sets the title of the notification.
        /// </summary>
        public required string Title { get; set; }

        /// <summary>
        /// Gets or sets the message of the notification.
        /// </summary>
        public required string Message { get; set; }

        /// <summary>
        /// Gets or sets the exception of the notification.
        /// </summary>
        public required string Exception { get; set; }

        /// <summary>
        /// Gets or sets the backup ID of the notification.
        /// </summary>
        public required string BackupID { get; set; }

        /// <summary>
        /// Gets or sets the action of the notification.
        /// </summary>
        public required string Action { get; set; }

        /// <summary>
        /// Gets or sets the timestamp of the notification.
        /// </summary>
        public required DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets the log entry ID of the notification.
        /// </summary>
        public required string LogEntryID { get; set; }

        /// <summary>
        /// Gets or sets the message ID of the notification.
        /// </summary>
        public required string MessageID { get; set; }

        /// <summary>
        /// Gets or sets the message log tag of the notification.
        /// </summary>
        public required string MessageLogTag { get; set; }

    }
}