using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace IsthereanydealCollectionSync.Models
{
    public class SyncException : Exception
    {
        public  SyncException()
        {
        }

        public  SyncException(string message) : base(message)
        {
        }

        public  SyncException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
