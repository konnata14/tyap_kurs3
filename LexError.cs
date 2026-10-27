namespace tyap_2
{
    public class LexError
    {
        public int Index { get; set; }
        public string Type { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; }

        public LexError(int index, string type, int line, int column, string message)
        {
            Index = index;
            Type = type;
            Line = line;
            Column = column;
            Message = message;
        }
    }
}