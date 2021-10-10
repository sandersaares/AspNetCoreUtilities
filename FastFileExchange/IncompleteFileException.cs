namespace FastFileExchange
{
    /// <summary>
    /// Thrown when a file version being downloaded from the exchange was never and will never be completely uploaded.
    /// </summary>
    [Serializable]
    public sealed class IncompleteFileException : Exception
    {
        public IncompleteFileException() { }
        public IncompleteFileException(string message) : base(message) { }
        public IncompleteFileException(string message, Exception inner) : base(message, inner) { }
    }
}
