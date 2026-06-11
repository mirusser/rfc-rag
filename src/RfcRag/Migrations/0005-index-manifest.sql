create table rfc_rag.index_manifest
(
    id                   uuid primary key default gen_random_uuid(),
    mirror_path          text not null,
    parser_type          text not null,
    parser_version       text not null,
    embedding_provider   text not null,
    embedding_model      text not null,
    embedding_dimensions int  not null,
    embedding_batch_size int  not null,
    rfc_count            int  not null,
    section_count        int  not null,
    created_at           timestamptz not null default now()
);
