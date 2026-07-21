using System;
using System.Collections.Generic;
using System.IO;

namespace FlexTool;

/// <summary>
/// Enhanced error handling for mod manager operations.
/// Provides consistent error logging and user-friendly messages.
/// </summary>
public class ModManagerErrorHandler
{
    private readonly List<ModManagerError> _errors = new();
    private readonly Action<string, string, ToastService.ToastType> _toastCallback;

    public ModManagerErrorHandler(Action<string, string, ToastService.ToastType> toastCallback = null)
    {
        _toastCallback = toastCallback;
    }

    /// <summary>
    /// Safely executes an action with error handling.
    /// </summary>
    public bool TrySafe(Action action, string operationName, string errorMessageKey = null)
    {
        try
        {
            action?.Invoke();
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogAndShowError(operationName, $"Access denied: {ex.Message}", errorMessageKey ?? "Access Denied");
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            LogAndShowError(operationName, $"Directory not found: {ex.Message}", ModManagerConstants.ErrorMessages.MOD_FOLDER_NOT_FOUND);
            return false;
        }
        catch (FileNotFoundException ex)
        {
            LogAndShowError(operationName, $"File not found: {ex.Message}", ModManagerConstants.ErrorMessages.MOD_NOT_INSTALLED);
            return false;
        }
        catch (IOException ex)
        {
            LogAndShowError(operationName, $"IO error: {ex.Message}", ModManagerConstants.WarningMessages.MOD_FOLDER_LOCKED);
            return false;
        }
        catch (Exception ex)
        {
            LogAndShowError(operationName, $"Unexpected error: {ex.Message}", errorMessageKey ?? "Error");
            return false;
        }
    }

    /// <summary>
    /// Safely executes a function with error handling.
    /// </summary>
    public bool TrySafe<T>(Func<T> func, out T result, string operationName, string errorMessageKey = null) where T : class
    {
        result = null;
        try
        {
            result = func?.Invoke();
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            LogAndShowError(operationName, $"Access denied: {ex.Message}", errorMessageKey ?? "Access Denied");
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            LogAndShowError(operationName, $"Directory not found: {ex.Message}", ModManagerConstants.ErrorMessages.MOD_FOLDER_NOT_FOUND);
            return false;
        }
        catch (FileNotFoundException ex)
        {
            LogAndShowError(operationName, $"File not found: {ex.Message}", ModManagerConstants.ErrorMessages.MOD_NOT_INSTALLED);
            return false;
        }
        catch (IOException ex)
        {
            LogAndShowError(operationName, $"IO error: {ex.Message}", ModManagerConstants.WarningMessages.MOD_FOLDER_LOCKED);
            return false;
        }
        catch (Exception ex)
        {
            LogAndShowError(operationName, $"Unexpected error: {ex.Message}", errorMessageKey ?? "Error");
            return false;
        }
    }

    /// <summary>
    /// Logs an error and optionally shows a toast notification.
    /// </summary>
    private void LogAndShowError(string operation, string technicalMessage, string userMessage)
    {
        var error = new ModManagerError
        {
            Operation = operation,
            TechnicalMessage = technicalMessage,
            UserMessage = userMessage,
            Timestamp = DateTime.Now
        };

        _errors.Add(error);

        // Log technical message (for debugging)
        System.Diagnostics.Debug.WriteLine($"[ModManager Error] {operation}: {technicalMessage}");

        // Show user-friendly toast notification
        _toastCallback?.Invoke("Error", userMessage, ToastService.ToastType.Error);
    }

    /// <summary>
    /// Gets all logged errors.
    /// </summary>
    public List<ModManagerError> GetErrors() => new(_errors);

    /// <summary>
    /// Clears error history.
    /// </summary>
    public void ClearErrors() => _errors.Clear();

    /// <summary>
    /// Gets the last error, if any.
    /// </summary>
    public ModManagerError GetLastError() => _errors.Count > 0 ? _errors[^1] : null;
}

/// <summary>
/// Represents a logged error in mod manager operations.
/// </summary>
public class ModManagerError
{
    public string Operation { get; set; }
    public string TechnicalMessage { get; set; }
    public string UserMessage { get; set; }
    public DateTime Timestamp { get; set; }

    public override string ToString()
    {
        return $"[{Timestamp:HH:mm:ss}] {Operation}: {UserMessage}";
    }
}
