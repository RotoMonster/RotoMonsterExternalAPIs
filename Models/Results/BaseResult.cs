using System;
using System.Collections.Generic;
using System.Text;

namespace RotoMonsterExternalAPIs.Client.Models.Results
{
    public abstract class BaseResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }

        public static T Failure<T>(string errorMessage) where T : BaseResult, new()
        {
            return new T
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
