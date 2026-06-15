using RfcRag.Answering;

namespace RfcRag.Tests.UnitTests;

public sealed class CitationVerifierTests
{
    [Fact]
    public void Verify_NullAnswer_ReturnsEmptyResult()
    {
        var answer = new GeneratedAnswer
        {
            Answer = null!,
            Citations = [],
        };
        var pack = new EvidencePack();

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Empty(result.Claims);
        Assert.Equal(0.0, result.ClaimSupportRate);
        Assert.Empty(result.VerificationWarnings);
    }

    [Fact]
    public void Verify_EmptyAnswer_ReturnsEmptyResult()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "",
            Citations = [],
        };
        var pack = new EvidencePack();

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Empty(result.Claims);
        Assert.Equal(0.0, result.ClaimSupportRate);
        Assert.Empty(result.VerificationWarnings);
    }

    [Fact]
    public void Verify_SupportedClaim_ReturnsSupportedStatus()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "HTTP semantics are defined in RFC 9110 [9110#9.3.1].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#9.3.1",
                    RfcNumber = 9110,
                    Section = "9.3.1",
                    RelevantText = "HTTP semantics are defined.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#9.3.1",
                    RfcNumber = 9110,
                    Section = "9.3.1",
                    Text = "HTTP semantics are defined. This section covers the core protocol.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("HTTP semantics are defined in RFC 9110 [9110#9.3.1].", result.Claims[0].Claim);
        Assert.Equal("supported", result.Claims[0].Status);
        Assert.Equal(["9110#9.3.1"], result.Claims[0].CitationEvidenceIds);
        Assert.Equal(1.0, result.ClaimSupportRate);
        Assert.Empty(result.VerificationWarnings);
    }

    [Fact]
    public void Verify_UnsupportedClaim_MismatchedText_ReturnsUnsupportedStatus()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "HTTP/2 uses multiplexing [9110#6.1].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#6.1",
                    RfcNumber = 9110,
                    Section = "6.1",
                    RelevantText = "Multiplexing allows multiple streams.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#6.1",
                    RfcNumber = 9110,
                    Section = "6.1",
                    Text = "This section defines header compression, not multiplexing.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("unsupported", result.Claims[0].Status);
        Assert.Equal(0.0, result.ClaimSupportRate);
        Assert.Single(result.VerificationWarnings);
        Assert.Equal("verification_warning", result.VerificationWarnings[0].Type);
        Assert.Contains("could not be verified", result.VerificationWarnings[0].Message);
    }

    [Fact]
    public void Verify_ClaimWithNoCitations_ReturnsUncitedStatusAndWarning()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "This is a claim without any citation marker.",
            Citations = [],
        };
        var pack = new EvidencePack();

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("uncited", result.Claims[0].Status);
        Assert.Null(result.Claims[0].CitationEvidenceIds);
        Assert.Equal(0.0, result.ClaimSupportRate);
        Assert.Single(result.VerificationWarnings);
        Assert.Equal("verification_warning", result.VerificationWarnings[0].Type);
        Assert.Contains("no inline citations", result.VerificationWarnings[0].Message);
    }

    [Fact]
    public void Verify_MultipleClaims_MixedSupport_ReturnsCorrectRate()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 9110 defines HTTP semantics [9110#9.3.1]. HTTP/3 uses QUIC [9000#1]. This is an uncited claim.",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#9.3.1",
                    RfcNumber = 9110,
                    Section = "9.3.1",
                    RelevantText = "HTTP semantics are defined.",
                },
                new Citation
                {
                    EvidenceId = "9000#1",
                    RfcNumber = 9000,
                    Section = "1",
                    RelevantText = "QUIC is a transport protocol.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#9.3.1",
                    RfcNumber = 9110,
                    Section = "9.3.1",
                    Text = "HTTP semantics are defined in this section.",
                },
                new EvidenceSection
                {
                    EvidenceId = "9000#1",
                    RfcNumber = 9000,
                    Section = "1",
                    Text = "QUIC is a transport protocol built on UDP.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        // The answer splits into 3 claims: supported (9110 match), supported (9000 match after period normalization), uncited
        Assert.Equal(3, result.Claims.Count);
        Assert.Equal("supported", result.Claims[0].Status);
        Assert.Equal("supported", result.Claims[1].Status);
        Assert.Equal("uncited", result.Claims[2].Status);
        Assert.Equal(2.0 / 3, result.ClaimSupportRate);
        Assert.Single(result.VerificationWarnings); // only the uncited claim emits a warning
    }

    [Fact]
    public void Verify_MultipleCitationsInClaim_AnySupportedIsSupported()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "HTTP is defined across multiple specs [9110#9.3.1] [9000#2].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#9.3.1",
                    RfcNumber = 9110,
                    Section = "9.3.1",
                    RelevantText = "HTTP semantics.",
                },
                new Citation
                {
                    EvidenceId = "9000#2",
                    RfcNumber = 9000,
                    Section = "2",
                    RelevantText = "Stream multiplexing.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#9.3.1",
                    RfcNumber = 9110,
                    Section = "9.3.1",
                    Text = "HTTP semantics are defined here.",
                },
                new EvidenceSection
                {
                    EvidenceId = "9000#2",
                    RfcNumber = 9000,
                    Section = "2",
                    Text = "This section does not mention multiplexing.",
                },
            ],
        };

        // First citation matches, second doesn't → overall supported
        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("supported", result.Claims[0].Status);
        Assert.Equal(["9110#9.3.1", "9000#2"], result.Claims[0].CitationEvidenceIds);
        Assert.Equal(1.0, result.ClaimSupportRate);
        Assert.Empty(result.VerificationWarnings);
    }

    [Fact]
    public void Verify_NoCitationsNoneMatchEvidence_AllUnsupported()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 8446 defines TLS [8446#1].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "8446#1",
                    RfcNumber = 8446,
                    Section = "1",
                    RelevantText = "TLS 1.3 is a cryptographic protocol.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "8446#1",
                    RfcNumber = 8446,
                    Section = "1",
                    Text = "This section introduces the TLS 1.3 handshake.",
                },
            ],
        };

        // "TLS 1.3 is a cryptographic protocol" is NOT contained in
        // "This section introduces the TLS 1.3 handshake." → unsupported
        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("unsupported", result.Claims[0].Status);
        Assert.Equal(0.0, result.ClaimSupportRate);
        Assert.Single(result.VerificationWarnings);
    }

    [Fact]
    public void Verify_CitationWithNullRelevantText_SkippedAsUnsupported()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "HTTP caching is defined in RFC 9110 [9110#13].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#13",
                    RfcNumber = 9110,
                    Section = "13",
                    RelevantText = null,
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#13",
                    RfcNumber = 9110,
                    Section = "13",
                    Text = "Caching is a key HTTP mechanism.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("unsupported", result.Claims[0].Status);
        Assert.Equal(0.0, result.ClaimSupportRate);
    }

    [Fact]
    public void Verify_EvidenceIdNotInAnswerCitations_SkippedAsUnsupported()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "RFC 9110 defines caching [9110#13].",
            Citations = [], // no matching citation
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#13",
                    RfcNumber = 9110,
                    Section = "13",
                    Text = "Caching is a key HTTP mechanism.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Equal("unsupported", result.Claims[0].Status);
    }

    [Fact]
    public void Verify_LongClaim_TruncatesWarningMessage()
    {
        var longClaim = new string('A', 200) + " [9110#1].";
        var answer = new GeneratedAnswer
        {
            Answer = longClaim,
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    RelevantText = "Bogus text.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    Text = "Some evidence text that doesn't match.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.Single(result.VerificationWarnings);
        // Message should contain truncated claim plus "..."
        Assert.Contains("...", result.VerificationWarnings[0].Message);
        Assert.True(result.VerificationWarnings[0].Message.Length < longClaim.Length + 100,
            "Warning message should be shorter than the full claim + boilerplate");
    }

    [Fact]
    public void Verify_SentenceWithExclamationMark_StillSplitsCorrectly()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "Stop! HTTP is defined in RFC 9110 [9110#1].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    RelevantText = "HTTP is defined.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    Text = "HTTP is defined in this section. It covers protocol semantics.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Equal(2, result.Claims.Count);
        Assert.Equal("uncited", result.Claims[0].Status);   // "Stop!" has no citation
        Assert.Equal("supported", result.Claims[1].Status); // second claim has matching citation
        Assert.Equal(0.5, result.ClaimSupportRate);
    }

    [Fact]
    public void Verify_CitationEvidenceIdsArePopulated()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "See RFC 9110 [9110#1] and RFC 8446 [8446#2].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    RelevantText = "HTTP syntax.",
                },
                new Citation
                {
                    EvidenceId = "8446#2",
                    RfcNumber = 8446,
                    Section = "2",
                    RelevantText = "TLS handshake.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    Text = "HTTP syntax is defined here.",
                },
                new EvidenceSection
                {
                    EvidenceId = "8446#2",
                    RfcNumber = 8446,
                    Section = "2",
                    Text = "TLS handshake proceeds as follows.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Single(result.Claims);
        Assert.NotNull(result.Claims[0].CitationEvidenceIds);
        var evidenceIds = result.Claims[0].CitationEvidenceIds!;
        Assert.Equal(2, evidenceIds.Count);
        Assert.Contains("9110#1", evidenceIds);
        Assert.Contains("8446#2", evidenceIds);
    }

    [Fact]
    public void Verify_MultipleSentencesAllSupported_ReturnsOneDotZero()
    {
        var answer = new GeneratedAnswer
        {
            Answer = "HTTP semantics are in RFC 9110 [9110#1]. Caching is in RFC 9110 [9110#2].",
            Citations =
            [
                new Citation
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    RelevantText = "HTTP semantics.",
                },
                new Citation
                {
                    EvidenceId = "9110#2",
                    RfcNumber = 9110,
                    Section = "2",
                    RelevantText = "Caching overview.",
                },
            ],
        };
        var pack = new EvidencePack
        {
            Sections =
            [
                new EvidenceSection
                {
                    EvidenceId = "9110#1",
                    RfcNumber = 9110,
                    Section = "1",
                    Text = "HTTP semantics are the foundation of the protocol.",
                },
                new EvidenceSection
                {
                    EvidenceId = "9110#2",
                    RfcNumber = 9110,
                    Section = "2",
                    Text = "Caching overview is discussed later.",
                },
            ],
        };

        var result = CitationVerifier.Verify(answer, pack);

        Assert.Equal(2, result.Claims.Count);
        Assert.All(result.Claims, c => Assert.Equal("supported", c.Status));
        Assert.Equal(1.0, result.ClaimSupportRate);
        Assert.Empty(result.VerificationWarnings);
    }
}
