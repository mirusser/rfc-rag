namespace RfcRag.Settings;

/// <summary>Selects the RFC source format parser.</summary>
public enum RfcParserType
{
    /// <summary>Parse plain-text RFC files (.txt). Default.</summary>
    Text,

    /// <summary>
    /// Parse RFC XML 2 format files (.xml) in addition to plain-text files.
    /// TXT is always preferred; .xml is used only for RFC numbers that have
    /// no .txt counterpart. Requires RFC XML 2 (RFC 7991) source files.
    /// </summary>
    Xml
}
