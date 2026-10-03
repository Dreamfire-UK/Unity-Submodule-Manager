using System;
using UnityEngine;

namespace DreamfireSubmodules.Scripts.ServiceResult
{
    public sealed class ServiceResult
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string Error { get; }

        private ServiceResult(bool isSuccess, string error)
        {
            IsSuccess = isSuccess;
            Error = error ?? string.Empty;
        }

        public static ServiceResult Succeeded()
        {
            return new ServiceResult(true, string.Empty);
        }

        public static ServiceResult Failed(string error)
        {
            return new ServiceResult(false, NormaliseError(error));
        }

        private static string NormaliseError(string error)
        {
            return string.IsNullOrWhiteSpace(error) ? "The operation failed." : error.Trim();
        }
    }

    public sealed class ServiceResult<T>
    {
        private readonly T value;
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string Error { get; }

        public T Value
        {
            get
            {
                if (IsFailure) throw new InvalidOperationException("A failed result does not contain a value.");
                return value;
            }
        }

        private ServiceResult(bool isSuccess, T value, string error)
        {
            IsSuccess = isSuccess;
            this.value = value;
            Error = error ?? string.Empty;
        }

        public bool TryGetValue(out T resultValue)
        {
            resultValue = value;
            return IsSuccess;
        }

        public static ServiceResult<T> Succeeded(T value)
        {
            return new ServiceResult<T>(true, value, string.Empty);
        }

        public static ServiceResult<T> Failed(string error)
        {
            return new ServiceResult<T>(false, default, NormaliseError(error));
        }

        private static string NormaliseError(string error)
        {
            return string.IsNullOrWhiteSpace(error) ? "The operation failed." : error.Trim();
        }
    }
}