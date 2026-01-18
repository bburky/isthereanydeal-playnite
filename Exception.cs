using System;

namespace IsthereanydealCollectionSync
{
    public class ITADException : Exception
    {
        public ITADException() : base() {}
        public ITADException(string message) : base(message) { }
        public ITADException(string message, Exception inner) : base(message, inner) { }
    }
}
