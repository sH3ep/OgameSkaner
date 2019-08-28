using System;

namespace OgameSkaner.Model
{
    public class RestException:Exception
    {
        public RestException(string errorMessage) : base(errorMessage)
        {
            
        }
    }
}