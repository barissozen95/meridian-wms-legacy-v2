namespace MeridianWms.Web.Services;

// Business-rule violation; message is safe to show to callers (HTTP 400).
public class DomainException(string message) : Exception(message);

// Referenced entity does not exist (HTTP 404).
public class NotFoundException(string message) : Exception(message);
