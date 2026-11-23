namespace Helinho.Infrastructure.Email;

/// <summary>
/// <c>Brevo:ApiKey</c>, <c>Brevo:SenderEmail</c> e <c>Brevo:SenderName</c> (ADR-0003 §7).
/// É classe, não record: não tem <c>ToString</c> que imprima a chave.
/// </summary>
public sealed class EmailOptions
{
    public string ApiKey { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string SenderName { get; set; } = "";
}
