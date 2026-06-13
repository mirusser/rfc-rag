namespace RfcRag.Search;

/// <summary>
/// Read-side data access for RFC search operations.
/// Handles hybrid, lexical, vector, normative, and ABNF searches.
/// </summary>
internal sealed class SearchRepository(NpgsqlDataSource dataSource)
{

    private const string SectionProjection =
        """
        id as "Id",
        rfc_number as "RfcNumber",
        title as "Title",
        section as "Section",
        heading as "Heading",
        text as "Text",
        source_path as "SourcePath",
        url as "Url",
        source_sha256 as "SourceSha256",
        array[]::real[] as "Embedding"
        """;

    private const string SearchResultProjection =
        """
        rfc_sections.id as "Id",
        rfc_sections.rfc_number as "RfcNumber",
        rfc_sections.title as "Title",
        rfc_sections.section as "Section",
        rfc_sections.heading as "Heading",
        left(rfc_sections.text, 500) as "Excerpt",
        rfc_sections.source_path as "SourcePath",
        rfc_sections.url as "Url"
        """;

    private const int MaxLimit = 100;
    private const int CandidateExpansionFactor = 4;

    /// <summary>
    /// Full-text lexical search using PostgreSQL tsvector.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchLexicalAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        int normalizedLimit = NormalizeLimit(limit);
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select
                    {{SearchResultProjection}},
                    ts_rank(search_vector, plainto_tsquery('english', @Query))::float8 as "Score"
                from rfc_rag.rfc_sections
                where plainto_tsquery('english', @Query) @@ search_vector
                order by "Score" desc, rfc_number, section
                limit @Limit
                """,
                new { Query = query, Limit = normalizedLimit },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Vector similarity search using cosine distance (pgvector).
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchVectorAsync(
        float[] embedding,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        int normalizedLimit = NormalizeLimit(limit);
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select
                    {{SearchResultProjection}},
                    (1 / (1 + (embedding <=> cast(@Embedding as vector))))::float8 as "Score"
                from rfc_rag.rfc_sections
                where embedding is not null
                order by embedding <=> cast(@Embedding as vector), rfc_number, section
                limit @Limit
                """,
                new { Embedding = embedding, Limit = normalizedLimit },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Hybrid search combining vector similarity and full-text lexical search
    /// with reciprocal rank fusion (RRF).
    /// When <paramref name="normativeKeyword"/> is provided, only sections containing that
    /// uppercase normative keyword are eligible candidates (filtered inside both CTEs).
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchHybridAsync(
        string query,
        float[] embedding,
        int limit,
        string? normativeKeyword,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(embedding);

        int normalizedLimit = NormalizeLimit(limit);
        int candidateLimit = normalizedLimit * CandidateExpansionFactor;
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                with lexical as (
                    select id, row_number() over (order by ts_rank(search_vector, plainto_tsquery('english', @Query)) desc) as rank
                    from rfc_rag.rfc_sections
                    where plainto_tsquery('english', @Query) @@ search_vector
                      {{(normativeKeyword is not null ? "and exists (select 1 from rfc_rag.normative_occurrences o where o.section_id = rfc_sections.id and o.keyword = upper(@NormativeKeyword))" : "")}}
                    order by ts_rank(search_vector, plainto_tsquery('english', @Query)) desc
                    limit @CandidateLimit
                ),
                vector as (
                    select id, row_number() over (order by embedding <=> cast(@Embedding as vector)) as rank
                    from rfc_rag.rfc_sections
                    where embedding is not null
                      {{(normativeKeyword is not null ? "and exists (select 1 from rfc_rag.normative_occurrences o where o.section_id = rfc_sections.id and o.keyword = upper(@NormativeKeyword))" : "")}}
                    order by embedding <=> cast(@Embedding as vector)
                    limit @CandidateLimit
                ),
                fused as (
                    select
                        coalesce(lexical.id, vector.id) as id,
                        (coalesce(1.0 / (60 + lexical.rank), 0) + coalesce(1.0 / (60 + vector.rank), 0))::float8 as score
                    from lexical
                    {{(normativeKeyword is not null ? "left" : "full")}} join vector on lexical.id = vector.id
                )
                select
                    {{SearchResultProjection}},
                    fused.score as "Score"
                from fused
                join rfc_rag.rfc_sections on rfc_sections.id = fused.id
                order by fused.score desc, rfc_number, section
                limit @Limit
                """,
                new
                {
                    Query = query,
                    Embedding = embedding,
                    NormativeKeyword = normativeKeyword ?? string.Empty,
                    CandidateLimit = candidateLimit,
                    Limit = normalizedLimit
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Hybrid search returning a wider fused candidate set with both arm ranks and RRF score,
    /// suitable for application-side reranking. Returns up to <c>limit × 4</c> candidates.
    /// Callers not using the reranker should use <see cref="SearchHybridAsync"/> instead.
    /// </summary>
    public async Task<IReadOnlyList<HybridCandidate>> SearchHybridWideCandidatesAsync(
        string query,
        float[] embedding,
        int limit,
        string? normativeKeyword,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(embedding);

        int normalizedLimit = NormalizeLimit(limit);
        int candidateLimit = normalizedLimit * CandidateExpansionFactor;
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<HybridCandidate>(new CommandDefinition(
                $$"""
                with lexical as (
                    select id, row_number() over (order by ts_rank(search_vector, plainto_tsquery('english', @Query)) desc) as rank
                    from rfc_rag.rfc_sections
                    where plainto_tsquery('english', @Query) @@ search_vector
                      {{(normativeKeyword is not null ? "and exists (select 1 from rfc_rag.normative_occurrences o where o.section_id = rfc_sections.id and o.keyword = upper(@NormativeKeyword))" : "")}}
                    order by ts_rank(search_vector, plainto_tsquery('english', @Query)) desc
                    limit @CandidateLimit
                ),
                vector as (
                    select id, row_number() over (order by embedding <=> cast(@Embedding as vector)) as rank
                    from rfc_rag.rfc_sections
                    where embedding is not null
                      {{(normativeKeyword is not null ? "and exists (select 1 from rfc_rag.normative_occurrences o where o.section_id = rfc_sections.id and o.keyword = upper(@NormativeKeyword))" : "")}}
                    order by embedding <=> cast(@Embedding as vector)
                    limit @CandidateLimit
                ),
                fused as (
                    select
                        coalesce(lexical.id, vector.id) as id,
                        coalesce(lexical.rank, 0) as "LexicalRank",
                        coalesce(vector.rank, 0) as "VectorRank",
                        (coalesce(1.0 / (60 + lexical.rank), 0) + coalesce(1.0 / (60 + vector.rank), 0))::float8 as "RrfScore"
                    from lexical
                    {{(normativeKeyword is not null ? "left" : "full")}} join vector on lexical.id = vector.id
                )
                select
                    {{SearchResultProjection}},
                    fused."LexicalRank",
                    fused."VectorRank",
                    fused."RrfScore"
                from fused
                join rfc_rag.rfc_sections on rfc_sections.id = fused.id
                order by fused."RrfScore" desc, rfc_number, section
                """,
                new
                {
                    Query = query,
                    Embedding = embedding,
                    NormativeKeyword = normativeKeyword ?? string.Empty,
                    CandidateLimit = candidateLimit
                },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Retrieve a single section of an RFC by number and section identifier.
    /// </summary>
    public async Task<RfcSection?> GetSectionAsync(
        int rfcNumber,
        string section,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber and section = @Section
                """,
                new { RfcNumber = rfcNumber, Section = section },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Retrieve all sections of an RFC, ordered by section number.
    /// </summary>
    public async Task<IReadOnlyList<RfcSection>> GetRfcAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber
                order by
                  case when section ~ '^[0-9]+(\.[0-9]+)*$'
                    then string_to_array(section, '.')::int[]
                  end nulls last,
                  section
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Search for normative keywords (RFC 2119/8174) across indexed RFCs.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchNormativeAsync(
        string keyword,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select * from (
                    select distinct on (rfc_sections.id)
                        {{SearchResultProjection}},
                        (1.0 / (1 + occurrences.line_offset))::float8 as "Score"
                    from rfc_rag.normative_occurrences occurrences
                    join rfc_rag.rfc_sections rfc_sections on rfc_sections.id = occurrences.section_id
                    where occurrences.keyword = upper(@Keyword)
                      {{(rfcNumbers is not null ? "and occurrences.rfc_number = any(@RfcNumbers)" : "")}}
                    order by rfc_sections.id, occurrences.line_offset
                ) ranked
                order by "Score" desc, "RfcNumber", "Section"
                limit @Limit
                """,
                new { Keyword = keyword, RfcNumbers = rfcNumbers ?? [], Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Search ABNF grammar definitions by rule name or fragment.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchAbnfAsync(
        string query,
        int[]? rfcNumbers,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<SearchResult>(new CommandDefinition(
                $$"""
                select * from (
                    select distinct on (rfc_sections.id)
                        {{SearchResultProjection}},
                        ts_rank(blocks.search_vector, plainto_tsquery('english', @Query))::float8 as "Score"
                    from rfc_rag.rfc_abnf_blocks blocks
                    join rfc_rag.rfc_sections rfc_sections on rfc_sections.id = blocks.section_id
                    where (plainto_tsquery('english', @Query) @@ blocks.search_vector
                       or blocks.abnf_text ilike '%' || @Query || '%')
                      {{(rfcNumbers is not null ? "and blocks.rfc_number = any(@RfcNumbers)" : "")}}
                    order by rfc_sections.id, "Score" desc
                ) ranked
                order by "Score" desc, "RfcNumber", "Section"
                limit @Limit
                """,
                new { Query = query, RfcNumbers = rfcNumbers ?? [], Limit = NormalizeLimit(limit) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>Retrieves a section and its immediate child subsections.</summary>
    public async Task<(RfcSection Parent, IReadOnlyList<RfcSection> Children)> GetSectionWithChildrenAsync(
        int rfcNumber,
        string section,
        int depth,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            RfcSection? parent = await connection.QuerySingleOrDefaultAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber and section = @Section
                """,
                new { RfcNumber = rfcNumber, Section = section },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (parent is null)
                return (RfcSection.Empty, []);

            if (depth <= 0)
                return (parent, []);

            string sectionPattern = section + @"\.";
            var children = await connection.QueryAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber and section ~ ('^' || @SectionPattern || '[^.]+$')
                order by
                  case when section ~ '^[0-9]+(\.[0-9]+)*$'
                    then string_to_array(section, '.')::int[]
                  end nulls last,
                  section
                """,
                new { RfcNumber = rfcNumber, SectionPattern = sectionPattern },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return (parent, children.AsList());
        }
    }

    /// <summary>
    /// Finds sections within an RFC whose headings match the given type names.
    /// Used to locate type-definition sections for type-reference resolution.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, RfcSection>> FindSectionsByHeadingsAsync(
        int rfcNumber,
        IReadOnlyList<string> typeNames,
        CancellationToken cancellationToken)
    {
        if (typeNames.Count == 0)
            return new Dictionary<string, RfcSection>(StringComparer.Ordinal);

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var matches = await connection.QueryAsync<RfcSection>(new CommandDefinition(
                $$"""
                select {{SectionProjection}}
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber and heading = any(@TypeNames)
                """,
                new { RfcNumber = rfcNumber, TypeNames = typeNames.ToArray() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return matches
                .Where(r => r.Heading is not null)
                .GroupBy(r => r.Heading!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    /// <summary>
    /// Batch-fetches normative keyword occurrences for a set of section IDs.
    /// Returns a dictionary mapping each section ID to its list of occurrences.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>> GetNormativeOccurrencesBatchAsync(
        IReadOnlyList<Guid> sectionIds,
        CancellationToken cancellationToken)
    {
        if (sectionIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>();

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<(Guid SectionId, string Keyword, int LineOffset)>(new CommandDefinition(
                """
                select section_id, keyword, line_offset
                from rfc_rag.normative_occurrences
                where section_id = any(@SectionIds)
                order by section_id, line_offset
                """,
                new { SectionIds = sectionIds.ToArray() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var result = new Dictionary<Guid, IReadOnlyList<NormativeOccurrenceData>>();
            foreach (var group in rows.GroupBy(r => r.SectionId))
            {
                result[group.Key] = group
                    .Select(r => new NormativeOccurrenceData { Keyword = r.Keyword, LineOffset = r.LineOffset })
                    .ToList();
            }

            return result;
        }
    }


    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RfcErratum>>> GetErrataBatchAsync(
        IReadOnlyList<int> rfcNumbers,
        IReadOnlyCollection<string> statuses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rfcNumbers);
        ArgumentNullException.ThrowIfNull(statuses);

        int[] distinctRfcNumbers = rfcNumbers.Distinct().ToArray();
        string[] distinctStatuses = statuses
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctRfcNumbers.Length == 0 || distinctStatuses.Length == 0)
        {
            return new Dictionary<string, IReadOnlyList<RfcErratum>>(StringComparer.Ordinal);
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<RfcErratum>(new CommandDefinition(
                """
                select
                    errata_id as "ErrataId",
                    rfc_number as "RfcNumber",
                    section as "Section",
                    status as "Status",
                    original_text as "OriginalText",
                    corrected_text as "CorrectedText",
                    reported_date as "ReportedDate"
                from rfc_rag.rfc_errata
                where rfc_number = any(@RfcNumbers)
                  and status = any(@Statuses)
                order by errata_id
                """,
                new { RfcNumbers = distinctRfcNumbers, Statuses = distinctStatuses },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows
                .Where(erratum => !string.IsNullOrWhiteSpace(erratum.Section))
                .GroupBy(erratum => $"{erratum.RfcNumber}#{erratum.Section}", StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<RfcErratum>)group.ToArray(),
                    StringComparer.Ordinal);
        }
    }

    /// <summary>Retrieves the table of contents for an RFC as a section→heading map.</summary>
    public async Task<IReadOnlyDictionary<string, string?>> GetTocAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<(string, string?)>(new CommandDefinition(
                """
                select section, heading
                from rfc_rag.rfc_sections
                where rfc_number = @RfcNumber
                order by
                  case when section ~ '^[0-9]+(\.[0-9]+)*$'
                    then string_to_array(section, '.')::int[]
                  end nulls last,
                  section
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return rows.ToDictionary(r => r.Item1, r => r.Item2, StringComparer.Ordinal);
        }
    }
}
