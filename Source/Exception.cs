using System;

namespace IsthereanydealCollectionSync
{
    public class ITADException : Exception
    {
        public ITADException() : base() {}
        public ITADException(string message) : base(message) { }
        public ITADException(string message, Exception inner) : base(message, inner) { }


        // TODO: for localization of error messages:
        // * Either make a bunch of specific exceptions deriving from this one, and then you can look up localized messages by exception type
        // * Or add some type of "localization string" property to this exception, and then you can look up localized messages by this key
        // (or rethink how to localize error messages in general, but this seems pretty easy)
    }
}
