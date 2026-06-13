namespace RfcRag.Search;

/// <summary>
/// Read-side data access for RFC metadata operations.
/// Handles metadata retrieval, listing, back-reference lookup, and statistics.
/// </summary>
internal sealed class MetadataRepository(NpgsqlDataSource dataSource)
{

    /// <summary>
    /// Retrieve metadata for a specific RFC (title, updates, obsoletes).
    /// </summary>
    public async Task<RfcMetadata?> GetIndexedRfcMetadataAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleOrDefaultAsync<RfcMetadata>(new CommandDefinition(
                """
                select
                    rfc_number as "Number",
                    title as "Title",
                    updates as "Updates",
                    obsoletes as "Obsoletes",
                    rfc_date as "Date",
                    category as "Category",
                    authors as "Authors",
                    issn as "Issn",
                    grammar_style as "GrammarStyle"
                from rfc_rag.indexed_rfcs
                where rfc_number = @RfcNumber
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// List indexed RFCs with pagination.
    /// </summary>
    public async Task<IReadOnlyList<RfcMetadata>> ListIndexedAsync(
        int limit,
        int offset,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<RfcMetadata>(new CommandDefinition(
                """
                select
                    rfc_number as "Number",
                    title as "Title"
                from rfc_rag.indexed_rfcs
                order by rfc_number
                limit @Limit
                offset @Offset
                """,
                new { Limit = Math.Clamp(limit, 1, 1000), Offset = Math.Max(0, offset) },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Find RFCs that reference this RFC number in their updates or obsoletes arrays.
    /// </summary>
    public async Task<IReadOnlyList<RfcMetadata>> FindBackReferencesAsync(
        int rfcNumber,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var results = await connection.QueryAsync<RfcMetadata>(new CommandDefinition(
                """
                select
                    rfc_number as "Number",
                    title as "Title",
                    updates as "Updates",
                    obsoletes as "Obsoletes"
                from rfc_rag.indexed_rfcs
                where @RfcNumber = any(updates) or @RfcNumber = any(obsoletes)
                order by rfc_number
                """,
                new { RfcNumber = rfcNumber },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return results.AsList();
        }
    }

    /// <summary>
    /// Batch lookup of forward and back references for a set of RFC numbers.
    /// Returns relation data for every RFC that has any relationship edge.
    /// One round trip for N RFCs, not N round trips.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, RfcRelationsBatch>> GetRelationsBatchAsync(
        IReadOnlyList<int> rfcNumbers,
        CancellationToken cancellationToken)
    {
        if (rfcNumbers.Count == 0)
            return new Dictionary<int, RfcRelationsBatch>();

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            // Forward + back references in a single round trip
            var multi = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                select
                    rfc_number as "RfcNumber",
                    updates as "Updates",
                    obsoletes as "Obsoletes"
                from rfc_rag.indexed_rfcs
                where rfc_number = any(@RfcNumbers);

                select distinct on (br_val, i.rfc_number)
                    br_val as "RfcNumber",
                    i.rfc_number as "Reference",
                    case
                        when br_val = any(i.updates) then 'updated_by'
                        when br_val = any(i.obsoletes) then 'obsoleted_by'
                    end as "Kind"
                from rfc_rag.indexed_rfcs i
                join lateral unnest(@RfcNumbers) as br_val on true
                where br_val = any(i.updates) or br_val = any(i.obsoletes)
                """,
                new { RfcNumbers = rfcNumbers.ToArray() },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var forward = await multi.ReadAsync<RfcRelationsBatch>().ConfigureAwait(false);
            var backRefs = await multi.ReadAsync<(int RfcNumber, int Reference, string Kind)>().ConfigureAwait(false);

            // Build the result dictionary, merging forward and back references
            var result = new Dictionary<int, RfcRelationsBatch>();
            foreach (var f in forward)
                result[f.RfcNumber] = f;

            foreach (var br in backRefs)
            {
                if (!result.TryGetValue(br.RfcNumber, out var rel))
                {
                    rel = new RfcRelationsBatch { RfcNumber = br.RfcNumber };
                    result[br.RfcNumber] = rel;
                }

                if (string.Equals(br.Kind, "updated_by", StringComparison.Ordinal))
                    rel = rel with { UpdatedBy = [.. rel.UpdatedBy, br.Reference] };
                else if (string.Equals(br.Kind, "obsoleted_by", StringComparison.Ordinal))
                    rel = rel with { ObsoletedBy = [.. rel.ObsoletedBy, br.Reference] };
                result[br.RfcNumber] = rel;
            }

            return result;
        }
    }

    /// <summary>
    /// Get statistics about the indexed RFC corpus.
    /// </summary>
    public async Task<string> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            return await connection.QuerySingleAsync<string>(new CommandDefinition(
                """
                select json_build_object(
                    'indexedRfcs', (select count(*) from rfc_rag.indexed_rfcs),
                    'sections', (select count(*) from rfc_rag.rfc_sections),
                    'abnfBlocks', (select count(*) from rfc_rag.rfc_abnf_blocks),
                'normativeOccurrences', (select count(*) from rfc_rag.normative_occurrences),
                'errata', (select count(*) from rfc_rag.rfc_errata),
                    'lastIndexedAtUtc', (select max(indexed_at_utc) from rfc_rag.indexed_rfcs),
                    'manifest', (
                        select row_to_json(m)
                        from (
                            select
                                id as "id",
                                parser_type as "parserType",
                                parser_version as "parserVersion",
                                embedding_provider as "embeddingProvider",
                                embedding_model as "embeddingModel",
                                embedding_dimensions as "embeddingDimensions",
                                rfc_count as "rfcCount",
                                section_count as "sectionCount",
                                created_at as "createdAt"
                            from rfc_rag.index_manifest
                            order by created_at desc
                            limit 1
                        ) m
                    )
                )::text
                """,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
        }
    }
}
