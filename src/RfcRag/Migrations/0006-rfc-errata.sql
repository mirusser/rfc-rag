create table rfc_rag.rfc_errata
(
    errata_id integer primary key,
    rfc_number integer not null,
    section text null,
    status text not null,
    original_text text null,
    corrected_text text null,
    reported_date date null
);

create index ix_rfc_errata_rfc_section_status
    on rfc_rag.rfc_errata (rfc_number, section, status);
