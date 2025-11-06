namespace Sage200Microservice.API.Models
{
    /// <summary>
    /// Exception thrown when validation fails
    /// </summary>
    public class ValidationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the ValidationException class
        /// </summary>
        public ValidationException() : base("One or more validation errors occurred.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the ValidationException class with a specified error message
        /// </summary>
        /// <param name="message"> The error message </param>
        public ValidationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ValidationException class with a specified error
        /// message and inner exception
        /// </summary>
        /// <param name="message">        The error message </param>
        /// <param name="innerException"> The inner exception </param>
        public ValidationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when a resource is not found
    /// </summary>
    public class ResourceNotFoundException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the ResourceNotFoundException class
        /// </summary>
        public ResourceNotFoundException() : base("The requested resource was not found.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the ResourceNotFoundException class with a specified error message
        /// </summary>
        /// <param name="message"> The error message </param>
        public ResourceNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the ResourceNotFoundException class with a specified
        /// resource name and identifier
        /// </summary>
        /// <param name="resourceName"> The name of the resource </param>
        /// <param name="resourceId">   The identifier of the resource </param>
        public ResourceNotFoundException(string resourceName, object resourceId)
            : base($"The {resourceName} with identifier {resourceId} was not found.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the ResourceNotFoundException class with a specified error
        /// message and inner exception
        /// </summary>
        /// <param name="message">        The error message </param>
        /// <param name="innerException"> The inner exception </param>
        public ResourceNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when authentication fails
    /// </summary>
    public class AuthenticationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the AuthenticationException class
        /// </summary>
        public AuthenticationException() : base("Authentication failed.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the AuthenticationException class with a specified error message
        /// </summary>
        /// <param name="message"> The error message </param>
        public AuthenticationException(string message) : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the AuthenticationException class with a specified error
        /// message and inner exception
        /// </summary>
        /// <param name="message">        The error message </param>
        /// <param name="innerException"> The inner exception </param>
        public AuthenticationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown when an external service call fails
    /// </summary>
    public class ExternalServiceException : Exception
    {
        /// <summary>
        /// The name of the external service
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Initializes a new instance of the ExternalServiceException class
        /// </summary>
        /// <param name="serviceName"> The name of the external service </param>
        public ExternalServiceException(string serviceName)
            : base($"An error occurred while calling the {serviceName} service.")
        {
            ServiceName = serviceName;
        }

        /// <summary>
        /// Initializes a new instance of the ExternalServiceException class with a specified error message
        /// </summary>
        /// <param name="serviceName"> The name of the external service </param>
        /// <param name="message">     The error message </param>
        public ExternalServiceException(string serviceName, string message)
            : base($"An error occurred while calling the {serviceName} service: {message}")
        {
            ServiceName = serviceName;
        }

        /// <summary>
        /// Initializes a new instance of the ExternalServiceException class with a specified error
        /// message and inner exception
        /// </summary>
        /// <param name="serviceName">    The name of the external service </param>
        /// <param name="message">        The error message </param>
        /// <param name="innerException"> The inner exception </param>
        public ExternalServiceException(string serviceName, string message, Exception innerException)
            : base($"An error occurred while calling the {serviceName} service: {message}", innerException)
        {
            ServiceName = serviceName;
        }
    }
}