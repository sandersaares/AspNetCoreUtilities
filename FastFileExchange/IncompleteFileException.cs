namespace FastFileExchange
{
    /// <summary>
    /// Thrown if the exchange has determined that a file being served is incomplete and will never be completed.
    /// </summary>
    [Serializable]
    public class IncompleteFileException : Exception
    {
        public IncompleteFileException() { }
        public IncompleteFileException(string message) : base(message) { }
        public IncompleteFileException(string message, Exception inner) : base(message, inner) { }
        protected IncompleteFileException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
    }
}
