PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

-- Aurora character loading schema:
-- This schema is optimized for character/feature loading first.
-- It captures the highest-value Aurora element families and the shared
-- grant/select/stat rule path that drives most character state.
--
-- Current tradeoffs:
-- - Descriptions are stored as text blobs instead of a normalized content DOM.
-- - Requirement/support expressions are preserved as raw text and a lightweight AST,
--   but are not yet evaluated by the importer itself.
-- - Lower-priority element families such as companions, deities, and lists
--   are intentionally deferred.
--
-- The next phase can normalize description markup and expression trees
-- without replacing the core element/rule shape defined here.

-- Tracks MD5 hashes of external import files (SRD JSON, etc.) for incremental updates.
CREATE TABLE IF NOT EXISTS import_state
(
    key         TEXT NOT NULL PRIMARY KEY,
    file_hash   TEXT NOT NULL,
    imported_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS source_books
(
    source_book_id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE IF NOT EXISTS content_packages
(
    content_package_id INTEGER PRIMARY KEY,
    package_key TEXT NOT NULL UNIQUE,
    package_name TEXT NOT NULL,
    package_kind TEXT NOT NULL DEFAULT 'local' CHECK
    (
        package_kind IN
        (
            'core',
            'official',
            'third-party',
            'homebrew',
            'local'
        )
    ),
    precedence_rank INTEGER NOT NULL DEFAULT 500,
    is_enabled INTEGER NOT NULL DEFAULT 1 CHECK (is_enabled IN (0, 1)),
    package_description TEXT,
    source_url TEXT,
    created_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_content_packages_precedence
    ON content_packages(is_enabled, precedence_rank DESC, package_kind, package_name);

CREATE TABLE IF NOT EXISTS source_files
(
    source_file_id  INTEGER PRIMARY KEY,
    relative_path   TEXT NOT NULL UNIQUE,
    content_package_id INTEGER REFERENCES content_packages(content_package_id),
    package_name    TEXT,
    package_description TEXT,
    version_text    TEXT,
    update_file_name TEXT,
    update_url      TEXT,
    author_name     TEXT,
    author_url      TEXT,
    -- MD5 of file contents; used to skip unchanged files on re-import.
    file_hash       TEXT
);

CREATE INDEX IF NOT EXISTS ix_source_files_package ON source_files(content_package_id, relative_path);

CREATE TABLE IF NOT EXISTS resolved_elements_cache
(
    aurora_id TEXT NOT NULL PRIMARY KEY,
    winning_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    source_file_id INTEGER REFERENCES source_files(source_file_id) ON DELETE CASCADE,
    content_package_id INTEGER REFERENCES content_packages(content_package_id),
    package_key TEXT,
    package_name TEXT,
    package_kind TEXT,
    precedence_rank INTEGER,
    duplicate_count INTEGER NOT NULL,
    resolution_rank INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_resolved_elements_cache_element
    ON resolved_elements_cache(winning_element_id, content_package_id);
CREATE INDEX IF NOT EXISTS ix_resolved_elements_cache_package
    ON resolved_elements_cache(content_package_id, aurora_id);

CREATE TABLE IF NOT EXISTS resolved_unique_element_names_cache
(
    normalized_name TEXT NOT NULL PRIMARY KEY,
    winning_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    name TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_resolved_unique_names_element
    ON resolved_unique_element_names_cache(winning_element_id);

CREATE TABLE IF NOT EXISTS element_types
(
    element_type_id INTEGER PRIMARY KEY,
    type_name TEXT NOT NULL UNIQUE,
    loader_family TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS elements
(
    element_id INTEGER PRIMARY KEY,
    aurora_id TEXT NOT NULL,
    element_type_id INTEGER NOT NULL REFERENCES element_types(element_type_id),
    source_book_id INTEGER REFERENCES source_books(source_book_id),
    source_file_id INTEGER REFERENCES source_files(source_file_id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    slug TEXT NOT NULL,
    compendium_display INTEGER NOT NULL DEFAULT 1 CHECK (compendium_display IN (0, 1)),
    loader_priority INTEGER NOT NULL DEFAULT 100,
    created_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_elements_type_name ON elements(element_type_id, name);
CREATE INDEX IF NOT EXISTS ix_elements_source_name ON elements(source_book_id, name);
CREATE INDEX IF NOT EXISTS ix_elements_slug ON elements(slug);
CREATE INDEX IF NOT EXISTS ix_elements_aurora_id ON elements(aurora_id);

CREATE TABLE IF NOT EXISTS element_texts
(
    element_text_id INTEGER PRIMARY KEY,
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    text_kind TEXT NOT NULL CHECK
    (
        text_kind IN
        (
            'description',
            'sheet',
            'prerequisite',
            'prerequisites',
            'multiclass-prerequisite',
            'summary'
        )
    ),
    ordinal INTEGER NOT NULL DEFAULT 1,
    level INTEGER,
    display INTEGER CHECK (display IN (0, 1)),
    alt_text TEXT,
    action_text TEXT,
    usage_text TEXT,
    body TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_element_texts_kind ON element_texts(element_id, text_kind, ordinal);

CREATE TABLE IF NOT EXISTS element_text_markup
(
    element_text_id INTEGER PRIMARY KEY REFERENCES element_texts(element_text_id) ON DELETE CASCADE,
    content_format TEXT NOT NULL DEFAULT 'aurora-xml' CHECK (content_format IN ('aurora-xml')),
    raw_xml TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS element_blocks
(
    element_block_id INTEGER PRIMARY KEY,
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    block_name TEXT NOT NULL,
    body_text TEXT,
    raw_xml TEXT NOT NULL,
    UNIQUE (element_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_element_blocks_name ON element_blocks(block_name, element_id);

CREATE TABLE IF NOT EXISTS element_block_attributes
(
    element_block_id INTEGER NOT NULL REFERENCES element_blocks(element_block_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    attribute_name TEXT NOT NULL,
    attribute_value TEXT,
    PRIMARY KEY (element_block_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_element_block_attributes_name
    ON element_block_attributes(attribute_name, element_block_id);

CREATE TABLE IF NOT EXISTS element_supports
(
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_text TEXT NOT NULL,
    PRIMARY KEY (element_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_element_supports_text ON element_supports(support_text);

CREATE TABLE IF NOT EXISTS element_requirements
(
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    requirement_text TEXT NOT NULL,
    PRIMARY KEY (element_id, ordinal)
);

CREATE TABLE IF NOT EXISTS expressions
(
    expression_id INTEGER PRIMARY KEY,
    raw_text TEXT NOT NULL UNIQUE,
    normalized_text TEXT NOT NULL,
    parse_status TEXT NOT NULL CHECK (parse_status IN ('parsed', 'failed')),
    error_text TEXT
);

CREATE INDEX IF NOT EXISTS ix_expressions_normalized ON expressions(normalized_text, parse_status);

CREATE TABLE IF NOT EXISTS expression_nodes
(
    expression_node_id INTEGER PRIMARY KEY,
    expression_id INTEGER NOT NULL REFERENCES expressions(expression_id) ON DELETE CASCADE,
    parent_node_id INTEGER REFERENCES expression_nodes(expression_node_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    node_kind TEXT NOT NULL CHECK (node_kind IN ('and', 'or', 'not', 'value')),
    value_type TEXT,
    value_text TEXT
);

CREATE INDEX IF NOT EXISTS ix_expression_nodes_expression ON expression_nodes(expression_id, parent_node_id, ordinal);
CREATE INDEX IF NOT EXISTS ix_expression_nodes_value ON expression_nodes(value_type, value_text);

CREATE TABLE IF NOT EXISTS expression_usages
(
    expression_usage_id INTEGER PRIMARY KEY,
    expression_id INTEGER NOT NULL REFERENCES expressions(expression_id) ON DELETE CASCADE,
    owner_kind TEXT NOT NULL,
    owner_id INTEGER NOT NULL,
    field_name TEXT NOT NULL,
    ordinal INTEGER NOT NULL DEFAULT 1,
    source_text TEXT NOT NULL,
    UNIQUE (owner_kind, owner_id, field_name, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_expression_usages_expression ON expression_usages(expression_id, owner_kind, field_name);
CREATE INDEX IF NOT EXISTS ix_expression_usages_owner ON expression_usages(owner_kind, owner_id, field_name);

CREATE TABLE IF NOT EXISTS support_tags
(
    support_tag_id INTEGER PRIMARY KEY,
    support_text TEXT NOT NULL UNIQUE,
    normalized_text TEXT NOT NULL,
    support_kind TEXT NOT NULL DEFAULT 'unclassified' CHECK
    (
        support_kind IN
        (
            'unclassified',
            'direct-parent',
            'bounded-option-set',
            'broad-option-set',
            'dynamic-expression'
        )
    )
);

CREATE INDEX IF NOT EXISTS ix_support_tags_normalized ON support_tags(normalized_text);

CREATE TABLE IF NOT EXISTS element_support_links
(
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_tag_id INTEGER NOT NULL REFERENCES support_tags(support_tag_id),
    linked_element_id INTEGER REFERENCES elements(element_id) ON DELETE SET NULL,
    resolution_kind TEXT NOT NULL,
    is_primary_parent INTEGER NOT NULL DEFAULT 0 CHECK (is_primary_parent IN (0, 1)),
    PRIMARY KEY (element_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_element_support_links_tag ON element_support_links(support_tag_id, element_id);
CREATE INDEX IF NOT EXISTS ix_element_support_links_target ON element_support_links(linked_element_id, is_primary_parent);

CREATE TABLE IF NOT EXISTS classes
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    hit_die TEXT,
    short_text TEXT
);

CREATE TABLE IF NOT EXISTS source_elements
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    abbreviation_text TEXT,
    source_url TEXT,
    image_url TEXT,
    errata_url TEXT,
    author_name TEXT,
    author_abbreviation TEXT,
    author_url TEXT,
    is_official INTEGER CHECK (is_official IN (0, 1)),
    is_core INTEGER CHECK (is_core IN (0, 1)),
    is_supplement INTEGER CHECK (is_supplement IN (0, 1)),
    is_third_party INTEGER CHECK (is_third_party IN (0, 1)),
    release_text TEXT
);

CREATE TABLE IF NOT EXISTS class_multiclass
(
    class_element_id INTEGER PRIMARY KEY REFERENCES classes(element_id) ON DELETE CASCADE,
    multiclass_aurora_id TEXT,
    prerequisite_text TEXT,
    requirements_text TEXT,
    proficiencies_text TEXT
);

CREATE TABLE IF NOT EXISTS spellcasting_profiles
(
    spellcasting_profile_id INTEGER PRIMARY KEY,
    owner_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    owner_kind TEXT NOT NULL CHECK (owner_kind IN ('class', 'archetype', 'feature')),
    profile_name TEXT NOT NULL,
    ability_name TEXT,
    is_extended INTEGER NOT NULL DEFAULT 0 CHECK (is_extended IN (0, 1)),
    prepare_spells INTEGER CHECK (prepare_spells IN (0, 1)),
    allow_replace INTEGER CHECK (allow_replace IN (0, 1)),
    list_text TEXT,
    extend_text TEXT,
    UNIQUE (owner_element_id, owner_kind)
);

CREATE TABLE IF NOT EXISTS archetypes
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    parent_class_element_id INTEGER REFERENCES classes(element_id) ON DELETE SET NULL,
    parent_support_text TEXT
);

CREATE TABLE IF NOT EXISTS races
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    names_format_text TEXT
);

CREATE TABLE IF NOT EXISTS race_name_groups
(
    race_element_id INTEGER NOT NULL REFERENCES races(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    name_group_type TEXT NOT NULL,
    name_value TEXT NOT NULL,
    PRIMARY KEY (race_element_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_race_name_groups_type ON race_name_groups(race_element_id, name_group_type);

CREATE TABLE IF NOT EXISTS subraces
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    race_element_id INTEGER REFERENCES races(element_id) ON DELETE SET NULL,
    parent_support_text TEXT
);

CREATE TABLE IF NOT EXISTS race_variants
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    variant_kind TEXT NOT NULL CHECK (variant_kind IN ('Race Variant', 'Dragonmark')),
    race_element_id INTEGER REFERENCES races(element_id) ON DELETE SET NULL,
    parent_support_text TEXT
);

CREATE INDEX IF NOT EXISTS ix_race_variants_parent ON race_variants(race_element_id);

CREATE TABLE IF NOT EXISTS backgrounds
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS background_variants
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    background_element_id INTEGER REFERENCES backgrounds(element_id) ON DELETE SET NULL,
    parent_support_text TEXT
);

CREATE INDEX IF NOT EXISTS ix_background_variants_parent ON background_variants(background_element_id);

CREATE TABLE IF NOT EXISTS feats
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    allow_duplicate INTEGER CHECK (allow_duplicate IN (0, 1))
);

CREATE TABLE IF NOT EXISTS spells
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    spell_level INTEGER NOT NULL DEFAULT 0,
    school_name TEXT,
    casting_time_text TEXT,
    range_text TEXT,
    duration_text TEXT,
    has_verbal INTEGER NOT NULL DEFAULT 0 CHECK (has_verbal IN (0, 1)),
    has_somatic INTEGER NOT NULL DEFAULT 0 CHECK (has_somatic IN (0, 1)),
    has_material INTEGER NOT NULL DEFAULT 0 CHECK (has_material IN (0, 1)),
    material_text TEXT,
    is_concentration INTEGER NOT NULL DEFAULT 0 CHECK (is_concentration IN (0, 1)),
    is_ritual INTEGER NOT NULL DEFAULT 0 CHECK (is_ritual IN (0, 1)),
    attack_type TEXT,
    damage_type_text TEXT,
    damage_formula_text TEXT,
    dc_ability_name TEXT,
    dc_success_text TEXT,
    source_url TEXT
);

CREATE INDEX IF NOT EXISTS ix_spells_level_school ON spells(spell_level, school_name);

CREATE TABLE IF NOT EXISTS spell_access
(
    spell_element_id INTEGER NOT NULL REFERENCES spells(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    access_text TEXT NOT NULL,
    PRIMARY KEY (spell_element_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_spell_access_text ON spell_access(access_text);

CREATE TABLE IF NOT EXISTS languages
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    script_text TEXT,
    speakers_text TEXT,
    is_standard INTEGER NOT NULL DEFAULT 0 CHECK (is_standard IN (0, 1)),
    is_exotic INTEGER NOT NULL DEFAULT 0 CHECK (is_exotic IN (0, 1)),
    is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1))
);

CREATE TABLE IF NOT EXISTS proficiencies
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    proficiency_group TEXT,
    proficiency_subgroup TEXT
);

CREATE TABLE IF NOT EXISTS items
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    item_kind TEXT NOT NULL CHECK
    (
        item_kind IN ('Item', 'Weapon', 'Armor', 'Ammunition', 'Mount', 'Vehicle', 'Magic Item')
    ),
    cost_text TEXT,
    weight_text TEXT,
    damage_dice_text TEXT,
    damage_type_text TEXT,
    armor_class_text TEXT,
    properties_text TEXT,
    speed_text TEXT,
    capacity_text TEXT
);

CREATE TABLE IF NOT EXISTS features
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    feature_kind TEXT NOT NULL CHECK
    (
        feature_kind IN
        (
            'Class Feature',
            'Archetype Feature',
            'Racial Trait',
            'Background Feature',
            'Feat Feature',
            'Ability Score Improvement'
        )
    ),
    parent_element_id INTEGER REFERENCES elements(element_id) ON DELETE SET NULL,
    parent_support_text TEXT,
    min_level INTEGER
);

CREATE INDEX IF NOT EXISTS ix_features_parent ON features(parent_element_id, min_level);
CREATE INDEX IF NOT EXISTS ix_features_kind ON features(feature_kind, min_level);

CREATE TABLE IF NOT EXISTS parent_family_aliases
(
    alias_text TEXT NOT NULL,
    link_kind TEXT NOT NULL CHECK (link_kind IN ('feature-parent', 'archetype-parent')),
    target_name TEXT,
    target_type_name TEXT,
    target_aurora_id TEXT,
    resolution_kind TEXT NOT NULL DEFAULT 'target-name',
    priority INTEGER NOT NULL DEFAULT 100,
    PRIMARY KEY (alias_text, link_kind)
);

CREATE INDEX IF NOT EXISTS ix_parent_family_aliases_target_name
    ON parent_family_aliases(link_kind, target_name, target_type_name);

-- Setter scopes mirror rule scopes so raw Aurora <set> entries can be preserved
-- for normal elements and class multiclass blocks without polymorphic FKs.
CREATE TABLE IF NOT EXISTS setter_scopes
(
    setter_scope_id INTEGER PRIMARY KEY,
    owner_kind TEXT NOT NULL CHECK (owner_kind IN ('element', 'class-multiclass')),
    owner_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    scope_key TEXT NOT NULL,
    UNIQUE (owner_kind, owner_element_id, scope_key)
);

CREATE TABLE IF NOT EXISTS setter_entries
(
    setter_entry_id INTEGER PRIMARY KEY,
    setter_scope_id INTEGER NOT NULL REFERENCES setter_scopes(setter_scope_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    setter_name TEXT NOT NULL,
    setter_value TEXT,
    UNIQUE (setter_scope_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_setter_entries_name ON setter_entries(setter_name, setter_scope_id);

CREATE TABLE IF NOT EXISTS setter_entry_attributes
(
    setter_entry_id INTEGER NOT NULL REFERENCES setter_entries(setter_entry_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    attribute_name TEXT NOT NULL,
    attribute_value TEXT,
    PRIMARY KEY (setter_entry_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_setter_entry_attributes_name ON setter_entry_attributes(attribute_name, setter_entry_id);

-- Preserve Aurora <extract> blocks for packs and similar composite elements.
CREATE TABLE IF NOT EXISTS element_extracts
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    description_text TEXT
);

CREATE TABLE IF NOT EXISTS element_extract_items
(
    extract_item_id INTEGER PRIMARY KEY,
    element_id INTEGER NOT NULL REFERENCES element_extracts(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    item_text TEXT,
    target_aurora_id TEXT,
    linked_element_id INTEGER REFERENCES elements(element_id) ON DELETE SET NULL,
    amount_text TEXT,
    UNIQUE (element_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_element_extract_items_target ON element_extract_items(target_aurora_id, linked_element_id);

CREATE TABLE IF NOT EXISTS element_extract_item_attributes
(
    extract_item_id INTEGER NOT NULL REFERENCES element_extract_items(extract_item_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    attribute_name TEXT NOT NULL,
    attribute_value TEXT,
    PRIMARY KEY (extract_item_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_element_extract_item_attributes_name
    ON element_extract_item_attributes(attribute_name, extract_item_id);

-- Rule scopes let the same grant/select/stat tables be reused for an
-- element's main rules and a class multiclass block without polymorphic FKs.
CREATE TABLE IF NOT EXISTS rule_scopes
(
    rule_scope_id INTEGER PRIMARY KEY,
    owner_kind TEXT NOT NULL CHECK (owner_kind IN ('element', 'class-multiclass')),
    owner_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    scope_key TEXT NOT NULL,
    UNIQUE (owner_kind, owner_element_id, scope_key)
);

CREATE TABLE IF NOT EXISTS grants
(
    grant_id INTEGER PRIMARY KEY,
    rule_scope_id INTEGER NOT NULL REFERENCES rule_scopes(rule_scope_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    grant_type TEXT NOT NULL,
    target_aurora_id TEXT,
    target_element_id INTEGER REFERENCES elements(element_id) ON DELETE SET NULL,
    target_semantic_key TEXT,
    target_semantic_kind TEXT,
    target_semantic_name TEXT,
    raw_xml TEXT,
    name_text TEXT,
    grant_level INTEGER,
    spellcasting_name TEXT,
    is_prepared INTEGER CHECK (is_prepared IN (0, 1)),
    requirements_text TEXT,
    UNIQUE (rule_scope_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_grants_target ON grants(target_aurora_id, grant_type);
CREATE INDEX IF NOT EXISTS ix_grants_owner_level ON grants(rule_scope_id, grant_level);
CREATE INDEX IF NOT EXISTS ix_grants_semantic ON grants(target_semantic_key, target_semantic_kind);

CREATE TABLE IF NOT EXISTS selects
(
    select_id INTEGER PRIMARY KEY,
    rule_scope_id INTEGER NOT NULL REFERENCES rule_scopes(rule_scope_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    select_type TEXT NOT NULL,
    name_text TEXT NOT NULL,
    supports_text TEXT,
    select_level INTEGER,
    number_to_choose INTEGER NOT NULL DEFAULT 1,
    default_choice_text TEXT,
    is_optional INTEGER NOT NULL DEFAULT 0 CHECK (is_optional IN (0, 1)),
    spellcasting_profile_id INTEGER REFERENCES spellcasting_profiles(spellcasting_profile_id) ON DELETE SET NULL,
    raw_xml TEXT,
    requirements_text TEXT,
    UNIQUE (rule_scope_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_selects_type ON selects(select_type, select_level);

CREATE TABLE IF NOT EXISTS select_supports
(
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_text TEXT NOT NULL,
    PRIMARY KEY (select_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_select_supports_text ON select_supports(support_text);

CREATE TABLE IF NOT EXISTS select_items
(
    select_item_id INTEGER PRIMARY KEY,
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    item_text TEXT,
    target_aurora_id TEXT,
    option_kind TEXT NOT NULL DEFAULT 'name-reference-candidate' CHECK
    (
        option_kind IN
        (
            'aurora-reference',
            'name-reference-candidate',
            'text-choice'
        )
    ),
    linked_element_id INTEGER REFERENCES elements(element_id) ON DELETE SET NULL,
    UNIQUE (select_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_select_items_target ON select_items(target_aurora_id, linked_element_id);
CREATE INDEX IF NOT EXISTS ix_select_items_kind ON select_items(option_kind, linked_element_id);

CREATE TABLE IF NOT EXISTS select_item_attributes
(
    select_item_id INTEGER NOT NULL REFERENCES select_items(select_item_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    attribute_name TEXT NOT NULL,
    attribute_value TEXT,
    PRIMARY KEY (select_item_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_select_item_attributes_name
    ON select_item_attributes(attribute_name, select_item_id);

CREATE TABLE IF NOT EXISTS select_support_links
(
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_tag_id INTEGER NOT NULL REFERENCES support_tags(support_tag_id),
    linked_element_id INTEGER REFERENCES elements(element_id) ON DELETE SET NULL,
    resolution_kind TEXT NOT NULL,
    PRIMARY KEY (select_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_select_support_links_tag ON select_support_links(support_tag_id, select_id);
CREATE INDEX IF NOT EXISTS ix_select_support_links_target ON select_support_links(linked_element_id);

CREATE TABLE IF NOT EXISTS select_option_links
(
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    option_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    support_tag_id INTEGER NOT NULL REFERENCES support_tags(support_tag_id),
    match_kind TEXT NOT NULL,
    PRIMARY KEY (select_id, option_element_id, support_tag_id, match_kind)
);

CREATE INDEX IF NOT EXISTS ix_select_option_links_select ON select_option_links(select_id, support_tag_id);
CREATE INDEX IF NOT EXISTS ix_select_option_links_option ON select_option_links(option_element_id, support_tag_id);

CREATE TABLE IF NOT EXISTS stats
(
    stat_id INTEGER PRIMARY KEY,
    rule_scope_id INTEGER NOT NULL REFERENCES rule_scopes(rule_scope_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    stat_name TEXT NOT NULL,
    value_expression_text TEXT,
    bonus_expression_text TEXT,
    equipped_expression_text TEXT,
    stat_level INTEGER,
    inline_display INTEGER NOT NULL DEFAULT 0 CHECK (inline_display IN (0, 1)),
    alt_text TEXT,
    raw_xml TEXT,
    requirements_text TEXT,
    UNIQUE (rule_scope_id, ordinal)
);

CREATE INDEX IF NOT EXISTS ix_stats_name_level ON stats(stat_name, stat_level);

-- Seed the highest-value element types for character and feature loading.
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Spell', 'spell');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Source', 'source');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Class', 'class');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Archetype', 'archetype');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Class Feature', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Archetype Feature', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Race', 'race');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Sub Race', 'race');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Racial Trait', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Background', 'background');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Background Feature', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Background Sub-Feature', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Background Variant', 'background');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Feat', 'feat');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Feat Feature', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Ability Score Improvement', 'feature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Language', 'language');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Proficiency', 'proficiency');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Item', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Weapon', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Armor', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Ammunition', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Mount', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Vehicle', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Magic Item', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Companion', 'companion');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Companion Action', 'companion');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Companion Reaction', 'companion');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Companion Trait', 'companion');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Companion Feature', 'companion');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Monster', 'creature');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Alignment', 'reference');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Background Characteristics', 'background');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Condition', 'reference');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Deity', 'reference');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Dragonmark', 'race');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Grants', 'grant');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Information', 'reference');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Magic School', 'reference');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Option', 'option');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Race Variant', 'race');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Rule', 'rule');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Support', 'support');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Vision', 'reference');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Weapon Group', 'item');
INSERT OR IGNORE INTO element_types (type_name, loader_family) VALUES ('Weapon Property', 'item');

CREATE VIEW IF NOT EXISTS v_resolved_elements AS
SELECT
    aurora_id,
    winning_element_id,
    source_file_id,
    content_package_id,
    package_key,
    package_name,
    package_kind,
    precedence_rank,
    duplicate_count,
    resolution_rank
FROM resolved_elements_cache;

CREATE VIEW IF NOT EXISTS v_resolved_unique_element_names AS
SELECT
    normalized_name,
    winning_element_id,
    name
FROM resolved_unique_element_names_cache;

CREATE VIEW IF NOT EXISTS v_duplicate_aurora_ids AS
WITH duplicate_ids AS
(
    SELECT
        e.aurora_id,
        COUNT(*) AS duplicate_count
    FROM elements AS e
    WHERE e.aurora_id IS NOT NULL
      AND trim(e.aurora_id) <> ''
    GROUP BY e.aurora_id
    HAVING COUNT(*) > 1
)
SELECT
    e.aurora_id,
    e.element_id,
    e.name,
    et.type_name,
    sf.relative_path,
    cp.package_key,
    cp.package_name,
    cp.package_kind,
    cp.precedence_rank,
    COALESCE(cp.is_enabled, 1) AS is_enabled,
    duplicate_ids.duplicate_count,
    CASE
        WHEN rec.winning_element_id = e.element_id THEN 1
        ELSE 0
    END AS is_winner
FROM duplicate_ids
JOIN elements AS e
    ON e.aurora_id = duplicate_ids.aurora_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = e.source_file_id
LEFT JOIN content_packages AS cp
    ON cp.content_package_id = sf.content_package_id
LEFT JOIN resolved_elements_cache AS rec
    ON rec.aurora_id = e.aurora_id;

CREATE VIEW IF NOT EXISTS v_package_resolution_summary AS
WITH file_counts AS
(
    SELECT content_package_id, COUNT(*) AS file_count
    FROM source_files
    GROUP BY content_package_id
),
winner_counts AS
(
    SELECT content_package_id, COUNT(*) AS winning_element_count
    FROM resolved_elements_cache
    GROUP BY content_package_id
),
duplicate_counts AS
(
    SELECT
        sf.content_package_id,
        COUNT(*) AS duplicate_element_count,
        SUM(CASE WHEN dup.is_winner = 1 THEN 1 ELSE 0 END) AS duplicate_winner_count,
        SUM(CASE WHEN dup.is_winner = 0 THEN 1 ELSE 0 END) AS duplicate_loser_count
    FROM v_duplicate_aurora_ids AS dup
    JOIN source_files AS sf
        ON sf.relative_path = dup.relative_path
    GROUP BY sf.content_package_id
)
SELECT
    cp.content_package_id,
    cp.package_key,
    cp.package_name,
    cp.package_kind,
    cp.precedence_rank,
    cp.is_enabled,
    COALESCE(file_counts.file_count, 0) AS file_count,
    COALESCE(winner_counts.winning_element_count, 0) AS winning_element_count,
    COALESCE(duplicate_counts.duplicate_element_count, 0) AS duplicate_element_count,
    COALESCE(duplicate_counts.duplicate_winner_count, 0) AS duplicate_winner_count,
    COALESCE(duplicate_counts.duplicate_loser_count, 0) AS duplicate_loser_count
FROM content_packages AS cp
LEFT JOIN file_counts
    ON file_counts.content_package_id = cp.content_package_id
LEFT JOIN winner_counts
    ON winner_counts.content_package_id = cp.content_package_id
LEFT JOIN duplicate_counts
    ON duplicate_counts.content_package_id = cp.content_package_id;

CREATE VIEW IF NOT EXISTS v_unresolved_loader_links AS
SELECT
    'grant' AS link_kind,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(g.grant_id AS TEXT) AS link_id,
    g.target_aurora_id AS unresolved_key,
    g.name_text AS unresolved_text
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE g.target_aurora_id IS NOT NULL
  AND g.target_element_id IS NULL
  AND COALESCE(g.target_semantic_key, '') = ''

UNION ALL

SELECT
    'extract-item' AS link_kind,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(ei.extract_item_id AS TEXT) AS link_id,
    ei.target_aurora_id AS unresolved_key,
    ei.item_text AS unresolved_text
FROM element_extract_items AS ei
JOIN element_extracts AS ex
    ON ex.element_id = ei.element_id
JOIN elements AS owner
    ON owner.element_id = ex.element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE ei.linked_element_id IS NULL
  AND (ei.target_aurora_id IS NOT NULL OR ei.item_text IS NOT NULL)

UNION ALL

SELECT
    'select-item' AS link_kind,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(si.select_item_id AS TEXT) AS link_id,
    si.target_aurora_id AS unresolved_key,
    si.item_text AS unresolved_text
FROM select_items AS si
JOIN selects AS s
    ON s.select_id = si.select_id
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
WHERE si.linked_element_id IS NULL
  AND si.option_kind <> 'text-choice'
  AND (si.target_aurora_id IS NOT NULL OR si.item_text IS NOT NULL)

UNION ALL

SELECT
    'feature-parent' AS link_kind,
    feature.element_id AS owner_element_id,
    feature.aurora_id AS owner_aurora_id,
    feature.name AS owner_name,
    feature_type.type_name AS owner_type_name,
    CAST(feature.element_id AS TEXT) AS link_id,
    feature_meta.parent_support_text AS unresolved_key,
    feature_meta.parent_support_text AS unresolved_text
FROM features AS feature_meta
JOIN elements AS feature
    ON feature.element_id = feature_meta.element_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature.element_type_id
WHERE feature_meta.parent_support_text IS NOT NULL
  AND feature_meta.parent_element_id IS NULL

UNION ALL

SELECT
    'archetype-parent' AS link_kind,
    archetype.element_id AS owner_element_id,
    archetype.aurora_id AS owner_aurora_id,
    archetype.name AS owner_name,
    archetype_type.type_name AS owner_type_name,
    CAST(archetype.element_id AS TEXT) AS link_id,
    archetype_meta.parent_support_text AS unresolved_key,
    archetype_meta.parent_support_text AS unresolved_text
FROM archetypes AS archetype_meta
JOIN elements AS archetype
    ON archetype.element_id = archetype_meta.element_id
JOIN element_types AS archetype_type
    ON archetype_type.element_type_id = archetype.element_type_id
WHERE archetype_meta.parent_support_text IS NOT NULL
  AND archetype_meta.parent_class_element_id IS NULL;

CREATE VIEW IF NOT EXISTS v_unresolved_loader_link_diagnostics AS
WITH background_file_counts AS
(
    SELECT
        bg.source_file_id,
        COUNT(*) AS background_count
    FROM backgrounds AS b
    JOIN elements AS bg
        ON bg.element_id = b.element_id
    GROUP BY bg.source_file_id
),
feature_parent_family_counts AS
(
    SELECT
        unresolved_text,
        COUNT(*) AS family_count
    FROM v_unresolved_loader_links
    WHERE link_kind = 'feature-parent'
      AND unresolved_text IS NOT NULL
    GROUP BY unresolved_text
)
SELECT
    raw.link_kind,
    raw.owner_element_id,
    raw.owner_aurora_id,
    raw.owner_name,
    raw.owner_type_name,
    raw.link_id,
    raw.unresolved_key,
    raw.unresolved_text,
    CASE
        WHEN raw.link_kind = 'feature-parent'
         AND raw.unresolved_text = 'Background Feature'
         AND COALESCE(background_file_counts.background_count, 0) = 0
            THEN 'option-pool'
        WHEN raw.link_kind = 'feature-parent'
         AND
         (
             COALESCE(feature_parent_family_counts.family_count, 0) > 1
             OR raw.unresolved_text LIKE '%Option%'
             OR raw.unresolved_text LIKE 'PHB24 %'
             OR raw.unresolved_text LIKE 'Starry Form %'
             OR raw.unresolved_text LIKE 'Elemental Initiate %'
             OR raw.unresolved_text IN
                (
                    'BH Variant',
                    'MAgic of the Blade',
                    'Monster Type',
                    'Necromancer Variant Feature',
                    'Pactd Boon',
                    'vampire'
                )
         )
            THEN 'option-pool'
        WHEN raw.link_kind = 'archetype-parent'
         AND raw.unresolved_text = 'Training Paradigm'
            THEN 'missing-source'
        WHEN raw.link_kind = 'grant'
         AND COALESCE(trim(raw.unresolved_key), '') = ''
            THEN 'missing-source'
        ELSE 'actionable'
    END AS diagnostic_status,
    CASE
        WHEN raw.link_kind = 'feature-parent'
         AND raw.unresolved_text = 'Background Feature'
         AND COALESCE(background_file_counts.background_count, 0) = 0
            THEN 'background-feature-option-pool'
        WHEN raw.link_kind = 'feature-parent'
         AND
         (
             COALESCE(feature_parent_family_counts.family_count, 0) > 1
             OR raw.unresolved_text LIKE '%Option%'
             OR raw.unresolved_text LIKE 'PHB24 %'
             OR raw.unresolved_text LIKE 'Starry Form %'
             OR raw.unresolved_text LIKE 'Elemental Initiate %'
             OR raw.unresolved_text IN
                (
                    'BH Variant',
                    'MAgic of the Blade',
                    'Monster Type',
                    'Necromancer Variant Feature',
                    'Pactd Boon',
                    'vampire'
                )
         )
            THEN 'feature-family-option-pool'
        WHEN raw.link_kind = 'archetype-parent'
         AND raw.unresolved_text = 'Training Paradigm'
            THEN 'archetype-base-class-not-imported'
        WHEN raw.link_kind = 'grant'
         AND COALESCE(trim(raw.unresolved_key), '') = ''
            THEN 'grant-empty-target-id'
        ELSE NULL
    END AS diagnostic_reason
FROM v_unresolved_loader_links AS raw
LEFT JOIN elements AS owner
    ON owner.element_id = raw.owner_element_id
LEFT JOIN background_file_counts
    ON background_file_counts.source_file_id = owner.source_file_id
LEFT JOIN feature_parent_family_counts
    ON feature_parent_family_counts.unresolved_text = raw.unresolved_text;

CREATE VIEW IF NOT EXISTS v_source_integrity_issues AS
SELECT
    'grant-target-id-in-name-attribute' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(g.grant_id AS TEXT) AS issue_key,
    COALESCE(
        NULLIF(trim(g.raw_xml), ''),
        '<grant type="' || COALESCE(g.grant_type, '') || '" name="' || COALESCE(g.name_text, '') || '" />'
    ) AS issue_text
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(g.target_aurora_id), '') <> ''
  AND COALESCE(trim(g.name_text), '') <> ''
  AND upper(trim(g.name_text)) LIKE 'ID\_%' ESCAPE '\'
  AND trim(g.target_aurora_id) = trim(g.name_text)

UNION ALL

SELECT
    'blank-grant-target-id' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(g.grant_id AS TEXT) AS issue_key,
    COALESCE(
        NULLIF(trim(g.raw_xml), ''),
        '<grant type="' || COALESCE(g.grant_type, '') || '" id="" />'
    ) AS issue_text
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(g.target_aurora_id), '') = ''
  AND COALESCE(trim(g.grant_type), '') <> ''

UNION ALL

SELECT
    'blank-select-type' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(s.select_id AS TEXT) AS issue_key,
    COALESCE(s.raw_xml, '<select />') AS issue_text
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(s.select_type), '') = ''

UNION ALL

SELECT
    'blank-stat-name' AS issue_kind,
    sf.source_file_id,
    sf.relative_path,
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    CAST(st.stat_id AS TEXT) AS issue_key,
    COALESCE(st.raw_xml, '<stat />') AS issue_text
FROM stats AS st
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = st.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN source_files AS sf
    ON sf.source_file_id = owner.source_file_id
WHERE COALESCE(trim(st.stat_name), '') = ''

UNION ALL

SELECT
    'duplicate-element-id-in-file' AS issue_kind,
    dup.source_file_id,
    sf.relative_path,
    NULL AS owner_element_id,
    NULL AS owner_aurora_id,
    NULL AS owner_name,
    NULL AS owner_type_name,
    dup.aurora_id AS issue_key,
    'duplicate-count=' || dup.duplicate_count AS issue_text
FROM
(
    SELECT
        source_file_id,
        aurora_id,
        COUNT(*) AS duplicate_count
    FROM elements
    WHERE COALESCE(trim(aurora_id), '') <> ''
    GROUP BY source_file_id, aurora_id
    HAVING COUNT(*) > 1
) AS dup
JOIN source_files AS sf
    ON sf.source_file_id = dup.source_file_id;

-- Aurora companion stat blocks.
-- Populated from Aurora XML Companion elements (type="Companion").
-- ability scores are stored as integers; ac, hp, speed, skills, senses kept
-- as raw text because Aurora stores them as formatted strings (e.g. "5 (1d4 + 3)").
-- cr_value is the numeric CR for filtering (0.125 = 1/8, 0.25 = 1/4, 0.5 = 1/2).
-- actions_text is the raw comma-separated aurora_id list from the actions setter.
CREATE TABLE IF NOT EXISTS companions
(
    element_id              INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    size_text               TEXT,
    creature_type           TEXT,
    alignment               TEXT,
    ac_text                 TEXT,
    hp_text                 TEXT,
    speed_text              TEXT,
    str_score               INTEGER,
    dex_score               INTEGER,
    con_score               INTEGER,
    int_score               INTEGER,
    wis_score               INTEGER,
    cha_score               INTEGER,
    skills_text             TEXT,
    resistances_text        TEXT,
    immunities_text         TEXT,
    condition_immunities_text TEXT,
    senses_text             TEXT,
    languages_text          TEXT,
    challenge_text          TEXT,
    cr_value                REAL,
    proficiency_bonus       INTEGER,
    actions_text            TEXT
);

CREATE INDEX IF NOT EXISTS ix_companions_cr ON companions(cr_value, creature_type);
CREATE INDEX IF NOT EXISTS ix_companions_type ON companions(creature_type, size_text);

-- Unified creature table for SRD and external creature data.
-- Populated by the `srd-creatures` import command (5e-bits/5e-database JSON; formerly bagelbits/5e-database).
-- source_kind = 'srd'    : from the SRD 5.1 / 5e-database JSON
--             = 'aurora' : Aurora Companion element promoted to this table (no SRD match)
--             = 'custom' : manually added entries
CREATE TABLE IF NOT EXISTS creatures
(
    creature_id                 INTEGER PRIMARY KEY,
    name                        TEXT NOT NULL,
    slug                        TEXT NOT NULL,
    cr_text                     TEXT,
    cr_value                    REAL,
    size_text                   TEXT,
    creature_type               TEXT,
    subtype_text                TEXT,
    alignment                   TEXT,
    ac_text                     TEXT,
    hp_average                  INTEGER,
    hp_text                     TEXT,
    speed_text                  TEXT,
    str_score                   INTEGER,
    dex_score                   INTEGER,
    con_score                   INTEGER,
    int_score                   INTEGER,
    wis_score                   INTEGER,
    cha_score                   INTEGER,
    saving_throws_text          TEXT,
    skills_text                 TEXT,
    damage_vulnerabilities_text TEXT,
    damage_resistances_text     TEXT,
    damage_immunities_text      TEXT,
    condition_immunities_text   TEXT,
    senses_text                 TEXT,
    languages_text              TEXT,
    proficiency_bonus           INTEGER,
    source_kind                 TEXT NOT NULL DEFAULT 'srd' CHECK (source_kind IN ('srd', 'aurora', 'custom')),
    source_name                 TEXT,
    description_text            TEXT,
    created_utc                 TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_creatures_cr ON creatures(cr_value, creature_type);
CREATE INDEX IF NOT EXISTS ix_creatures_type ON creatures(creature_type, size_text);
CREATE INDEX IF NOT EXISTS ix_creatures_name ON creatures(name);
CREATE INDEX IF NOT EXISTS ix_creatures_slug ON creatures(slug);

-- Cross-reference between creatures (SRD/external data) and Aurora Companion elements.
-- link_kind = 'name-match' : automatically matched on normalized name equality
--           = 'exact'      : matched on a canonical known pairing
--           = 'manual'     : manually curated override
CREATE TABLE IF NOT EXISTS creature_aurora_links
(
    creature_id INTEGER NOT NULL REFERENCES creatures(creature_id) ON DELETE CASCADE,
    element_id  INTEGER NOT NULL REFERENCES elements(element_id)  ON DELETE CASCADE,
    link_kind   TEXT NOT NULL DEFAULT 'name-match' CHECK (link_kind IN ('name-match', 'exact', 'manual')),
    PRIMARY KEY (creature_id, element_id)
);

CREATE INDEX IF NOT EXISTS ix_creature_aurora_links_element ON creature_aurora_links(element_id);

-- Loader-centric views.

CREATE VIEW IF NOT EXISTS v_feature_loader AS
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    et.type_name,
    f.feature_kind,
    f.parent_element_id,
    parent.name AS parent_name,
    f.parent_support_text,
    f.min_level,
    sheet.alt_text,
    sheet.action_text,
    sheet.usage_text,
    sheet.body AS sheet_text,
    body.body AS description_text
FROM features AS f
JOIN elements AS e
    ON e.element_id = f.element_id
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
LEFT JOIN elements AS parent
    ON parent.element_id = f.parent_element_id
LEFT JOIN element_texts AS sheet
    ON sheet.element_id = e.element_id
   AND sheet.text_kind = 'sheet'
   AND sheet.ordinal = 1
LEFT JOIN element_texts AS body
    ON body.element_id = e.element_id
   AND body.text_kind = 'description'
   AND body.ordinal = 1;

CREATE VIEW IF NOT EXISTS v_element_support_loader AS
SELECT
    child.element_id AS child_element_id,
    child.aurora_id AS child_aurora_id,
    child.name AS child_name,
    child_type.type_name AS child_type_name,
    esl.ordinal,
    st.support_text,
    st.support_kind,
    esl.resolution_kind,
    esl.is_primary_parent,
    target.element_id AS target_element_id,
    target.aurora_id AS target_aurora_id,
    target.name AS target_name,
    target_type.type_name AS target_type_name
FROM element_support_links AS esl
JOIN support_tags AS st
    ON st.support_tag_id = esl.support_tag_id
JOIN elements AS child
    ON child.element_id = esl.element_id
JOIN element_types AS child_type
    ON child_type.element_type_id = child.element_type_id
LEFT JOIN elements AS target
    ON target.element_id = esl.linked_element_id
LEFT JOIN element_types AS target_type
    ON target_type.element_type_id = target.element_type_id;

CREATE VIEW IF NOT EXISTS v_support_tag_summary AS
SELECT
    st.support_tag_id,
    st.support_text,
    st.support_kind,
    COUNT(DISTINCT esl.element_id) AS supporting_element_count,
    COUNT(DISTINCT ssl.select_id) AS supporting_select_count,
    COUNT(DISTINCT sol.option_element_id) AS candidate_option_count,
    COUNT(DISTINCT esl.linked_element_id) AS resolved_target_count,
    MAX(CASE WHEN esl.is_primary_parent = 1 THEN 1 ELSE 0 END) AS has_primary_parent
FROM support_tags AS st
LEFT JOIN element_support_links AS esl
    ON esl.support_tag_id = st.support_tag_id
LEFT JOIN select_support_links AS ssl
    ON ssl.support_tag_id = st.support_tag_id
LEFT JOIN select_option_links AS sol
    ON sol.support_tag_id = st.support_tag_id
GROUP BY
    st.support_tag_id,
    st.support_text,
    st.support_kind;

DROP VIEW IF EXISTS v_class_feature_progression;
CREATE VIEW v_class_feature_progression AS
SELECT
    c.element_id AS class_element_id,
    class_element.aurora_id AS class_aurora_id,
    class_element.name AS class_name,
    class_rec.package_key AS class_package_key,
    class_sf.relative_path AS class_source_path,
    c.hit_die,
    c.short_text AS class_short_text,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    COALESCE(g.grant_level, feature_meta.min_level) AS unlock_level,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_rec.package_key AS feature_package_key,
    feature_sf.relative_path AS feature_source_path,
    feature_type.type_name AS feature_type_name,
    feature_meta.parent_element_id,
    feature_meta.parent_support_text,
    COALESCE(feature_summary.body, feature_sheet.body, feature_description.body) AS feature_summary_text
FROM classes AS c
JOIN resolved_elements_cache AS class_rec
    ON class_rec.winning_element_id = c.element_id
JOIN elements AS class_element
    ON class_element.element_id = c.element_id
JOIN source_files AS class_sf
    ON class_sf.source_file_id = class_element.source_file_id
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = c.element_id
   AND rs.scope_key = 'element'
JOIN grants AS g
    ON g.rule_scope_id = rs.rule_scope_id
JOIN elements AS feature_element
    ON feature_element.element_id = g.target_element_id
JOIN resolved_elements_cache AS feature_rec
    ON feature_rec.winning_element_id = feature_element.element_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature_element.element_type_id
JOIN source_files AS feature_sf
    ON feature_sf.source_file_id = feature_element.source_file_id
LEFT JOIN features AS feature_meta
    ON feature_meta.element_id = feature_element.element_id
LEFT JOIN element_texts AS feature_summary
    ON feature_summary.element_id = feature_element.element_id
   AND feature_summary.text_kind = 'summary'
   AND feature_summary.ordinal = 1
LEFT JOIN element_texts AS feature_sheet
    ON feature_sheet.element_id = feature_element.element_id
   AND feature_sheet.text_kind = 'sheet'
   AND feature_sheet.ordinal = 1
LEFT JOIN element_texts AS feature_description
    ON feature_description.element_id = feature_element.element_id
   AND feature_description.text_kind = 'description'
   AND feature_description.ordinal = 1
WHERE g.grant_type = 'Class Feature'
  AND g.target_element_id IS NOT NULL;

DROP VIEW IF EXISTS v_class_archetype_slots;
CREATE VIEW v_class_archetype_slots AS
SELECT
    cfp.class_element_id,
    cfp.class_aurora_id,
    cfp.class_name,
    cfp.class_package_key,
    cfp.class_source_path,
    cfp.unlock_level,
    cfp.feature_element_id AS slot_feature_element_id,
    cfp.feature_aurora_id AS slot_feature_aurora_id,
    cfp.feature_name AS slot_feature_name,
    cfp.feature_package_key AS slot_feature_package_key,
    cfp.feature_source_path AS slot_feature_source_path,
    cfp.feature_summary_text AS slot_feature_summary_text,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.number_to_choose,
    s.is_optional
FROM v_class_feature_progression AS cfp
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = cfp.feature_element_id
   AND rs.scope_key = 'element'
JOIN selects AS s
    ON s.rule_scope_id = rs.rule_scope_id
WHERE s.select_type = 'Archetype';

DROP VIEW IF EXISTS v_archetype_feature_progression;
CREATE VIEW v_archetype_feature_progression AS
SELECT
    a.element_id AS archetype_element_id,
    archetype_element.aurora_id AS archetype_aurora_id,
    archetype_element.name AS archetype_name,
    archetype_rec.package_key AS archetype_package_key,
    archetype_sf.relative_path AS archetype_source_path,
    class_element.element_id AS class_element_id,
    class_element.aurora_id AS class_aurora_id,
    class_element.name AS class_name,
    class_rec.package_key AS class_package_key,
    class_sf.relative_path AS class_source_path,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    COALESCE(g.grant_level, feature_meta.min_level) AS unlock_level,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_rec.package_key AS feature_package_key,
    feature_sf.relative_path AS feature_source_path,
    feature_type.type_name AS feature_type_name,
    COALESCE(feature_summary.body, feature_sheet.body, feature_description.body) AS feature_summary_text
FROM archetypes AS a
JOIN resolved_elements_cache AS archetype_rec
    ON archetype_rec.winning_element_id = a.element_id
JOIN elements AS archetype_element
    ON archetype_element.element_id = a.element_id
JOIN source_files AS archetype_sf
    ON archetype_sf.source_file_id = archetype_element.source_file_id
LEFT JOIN elements AS class_element
    ON class_element.element_id = a.parent_class_element_id
LEFT JOIN resolved_elements_cache AS class_rec
    ON class_rec.winning_element_id = class_element.element_id
LEFT JOIN source_files AS class_sf
    ON class_sf.source_file_id = class_element.source_file_id
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = a.element_id
   AND rs.scope_key = 'element'
JOIN grants AS g
    ON g.rule_scope_id = rs.rule_scope_id
JOIN elements AS feature_element
    ON feature_element.element_id = g.target_element_id
JOIN resolved_elements_cache AS feature_rec
    ON feature_rec.winning_element_id = feature_element.element_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature_element.element_type_id
JOIN source_files AS feature_sf
    ON feature_sf.source_file_id = feature_element.source_file_id
LEFT JOIN features AS feature_meta
    ON feature_meta.element_id = feature_element.element_id
LEFT JOIN element_texts AS feature_summary
    ON feature_summary.element_id = feature_element.element_id
   AND feature_summary.text_kind = 'summary'
   AND feature_summary.ordinal = 1
LEFT JOIN element_texts AS feature_sheet
    ON feature_sheet.element_id = feature_element.element_id
   AND feature_sheet.text_kind = 'sheet'
   AND feature_sheet.ordinal = 1
LEFT JOIN element_texts AS feature_description
    ON feature_description.element_id = feature_element.element_id
   AND feature_description.text_kind = 'description'
   AND feature_description.ordinal = 1
WHERE g.grant_type = 'Archetype Feature'
  AND g.target_element_id IS NOT NULL;

DROP VIEW IF EXISTS v_background_core;
CREATE VIEW v_background_core AS
SELECT
    b.element_id AS background_element_id,
    background_element.aurora_id AS background_aurora_id,
    background_element.name AS background_name,
    background_rec.package_key AS background_package_key,
    background_sf.relative_path AS background_source_path,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_rec.package_key AS feature_package_key,
    feature_sf.relative_path AS feature_source_path,
    COALESCE(background_summary.body, background_sheet.body, background_description.body) AS background_summary_text,
    COALESCE(feature_summary.body, feature_sheet.body, feature_description.body) AS feature_summary_text
FROM backgrounds AS b
JOIN resolved_elements_cache AS background_rec
    ON background_rec.winning_element_id = b.element_id
JOIN elements AS background_element
    ON background_element.element_id = b.element_id
JOIN source_files AS background_sf
    ON background_sf.source_file_id = background_element.source_file_id
LEFT JOIN element_texts AS background_summary
    ON background_summary.element_id = b.element_id
   AND background_summary.text_kind = 'summary'
   AND background_summary.ordinal = 1
LEFT JOIN element_texts AS background_sheet
    ON background_sheet.element_id = b.element_id
   AND background_sheet.text_kind = 'sheet'
   AND background_sheet.ordinal = 1
LEFT JOIN element_texts AS background_description
    ON background_description.element_id = b.element_id
   AND background_description.text_kind = 'description'
   AND background_description.ordinal = 1
LEFT JOIN features AS feature_meta
    ON feature_meta.parent_element_id = b.element_id
   AND feature_meta.feature_kind = 'Background Feature'
LEFT JOIN elements AS feature_element
    ON feature_element.element_id = feature_meta.element_id
LEFT JOIN resolved_elements_cache AS feature_rec
    ON feature_rec.winning_element_id = feature_element.element_id
LEFT JOIN source_files AS feature_sf
    ON feature_sf.source_file_id = feature_element.source_file_id
LEFT JOIN element_texts AS feature_summary
    ON feature_summary.element_id = feature_meta.element_id
   AND feature_summary.text_kind = 'summary'
   AND feature_summary.ordinal = 1
LEFT JOIN element_texts AS feature_sheet
    ON feature_sheet.element_id = feature_meta.element_id
   AND feature_sheet.text_kind = 'sheet'
   AND feature_sheet.ordinal = 1
LEFT JOIN element_texts AS feature_description
    ON feature_description.element_id = feature_meta.element_id
   AND feature_description.text_kind = 'description'
   AND feature_description.ordinal = 1
WHERE feature_element.element_id IS NULL
   OR feature_rec.winning_element_id = feature_element.element_id;

DROP VIEW IF EXISTS v_race_core;
CREATE VIEW v_race_core AS
SELECT
    r.element_id AS race_element_id,
    race_element.aurora_id AS race_aurora_id,
    race_element.name AS race_name,
    race_rec.package_key AS race_package_key,
    race_sf.relative_path AS race_source_path,
    COALESCE(r.names_format_text, '') AS names_format_text,
    COALESCE(race_summary.body, race_sheet.body, race_description.body) AS race_summary_text,
    (
        SELECT COUNT(*)
        FROM subraces AS sr
        JOIN resolved_elements_cache AS sr_rec
            ON sr_rec.winning_element_id = sr.element_id
        WHERE sr.race_element_id = r.element_id
    ) AS subrace_count,
    (
        SELECT COUNT(*)
        FROM race_variants AS rv
        JOIN resolved_elements_cache AS rv_rec
            ON rv_rec.winning_element_id = rv.element_id
        WHERE rv.race_element_id = r.element_id
    ) AS variant_count,
    (
        SELECT COUNT(*)
        FROM features AS f
        JOIN resolved_elements_cache AS feature_rec
            ON feature_rec.winning_element_id = f.element_id
        WHERE f.parent_element_id = r.element_id
          AND f.feature_kind IN ('Racial Trait', 'Dragonmark')
    ) AS racial_trait_count
FROM races AS r
JOIN resolved_elements_cache AS race_rec
    ON race_rec.winning_element_id = r.element_id
JOIN elements AS race_element
    ON race_element.element_id = r.element_id
JOIN source_files AS race_sf
    ON race_sf.source_file_id = race_element.source_file_id
LEFT JOIN element_texts AS race_summary
    ON race_summary.element_id = r.element_id
   AND race_summary.text_kind = 'summary'
   AND race_summary.ordinal = 1
LEFT JOIN element_texts AS race_sheet
    ON race_sheet.element_id = r.element_id
   AND race_sheet.text_kind = 'sheet'
   AND race_sheet.ordinal = 1
LEFT JOIN element_texts AS race_description
    ON race_description.element_id = r.element_id
   AND race_description.text_kind = 'description'
   AND race_description.ordinal = 1;

DROP VIEW IF EXISTS v_subrace_core;
CREATE VIEW v_subrace_core AS
SELECT
    sr.element_id AS subrace_element_id,
    subrace_element.aurora_id AS subrace_aurora_id,
    subrace_element.name AS subrace_name,
    subrace_rec.package_key AS subrace_package_key,
    subrace_sf.relative_path AS subrace_source_path,
    race_element.element_id AS race_element_id,
    race_element.aurora_id AS race_aurora_id,
    race_element.name AS race_name,
    race_rec.package_key AS race_package_key,
    race_sf.relative_path AS race_source_path,
    COALESCE(subrace_summary.body, subrace_sheet.body, subrace_description.body) AS subrace_summary_text
FROM subraces AS sr
JOIN resolved_elements_cache AS subrace_rec
    ON subrace_rec.winning_element_id = sr.element_id
JOIN elements AS subrace_element
    ON subrace_element.element_id = sr.element_id
JOIN source_files AS subrace_sf
    ON subrace_sf.source_file_id = subrace_element.source_file_id
LEFT JOIN elements AS race_element
    ON race_element.element_id = sr.race_element_id
LEFT JOIN resolved_elements_cache AS race_rec
    ON race_rec.winning_element_id = race_element.element_id
LEFT JOIN source_files AS race_sf
    ON race_sf.source_file_id = race_element.source_file_id
LEFT JOIN element_texts AS subrace_summary
    ON subrace_summary.element_id = sr.element_id
   AND subrace_summary.text_kind = 'summary'
   AND subrace_summary.ordinal = 1
LEFT JOIN element_texts AS subrace_sheet
    ON subrace_sheet.element_id = sr.element_id
   AND subrace_sheet.text_kind = 'sheet'
   AND subrace_sheet.ordinal = 1
LEFT JOIN element_texts AS subrace_description
    ON subrace_description.element_id = sr.element_id
   AND subrace_description.text_kind = 'description'
   AND subrace_description.ordinal = 1;

DROP VIEW IF EXISTS v_race_variant_core;
CREATE VIEW v_race_variant_core AS
SELECT
    rv.element_id AS variant_element_id,
    variant_element.aurora_id AS variant_aurora_id,
    variant_element.name AS variant_name,
    variant_rec.package_key AS variant_package_key,
    variant_sf.relative_path AS variant_source_path,
    race_element.element_id AS race_element_id,
    race_element.aurora_id AS race_aurora_id,
    race_element.name AS race_name,
    race_rec.package_key AS race_package_key,
    race_sf.relative_path AS race_source_path,
    COALESCE(variant_summary.body, variant_sheet.body, variant_description.body) AS variant_summary_text
FROM race_variants AS rv
JOIN resolved_elements_cache AS variant_rec
    ON variant_rec.winning_element_id = rv.element_id
JOIN elements AS variant_element
    ON variant_element.element_id = rv.element_id
JOIN source_files AS variant_sf
    ON variant_sf.source_file_id = variant_element.source_file_id
LEFT JOIN elements AS race_element
    ON race_element.element_id = rv.race_element_id
LEFT JOIN resolved_elements_cache AS race_rec
    ON race_rec.winning_element_id = race_element.element_id
LEFT JOIN source_files AS race_sf
    ON race_sf.source_file_id = race_element.source_file_id
LEFT JOIN element_texts AS variant_summary
    ON variant_summary.element_id = rv.element_id
   AND variant_summary.text_kind = 'summary'
   AND variant_summary.ordinal = 1
LEFT JOIN element_texts AS variant_sheet
    ON variant_sheet.element_id = rv.element_id
   AND variant_sheet.text_kind = 'sheet'
   AND variant_sheet.ordinal = 1
LEFT JOIN element_texts AS variant_description
    ON variant_description.element_id = rv.element_id
   AND variant_description.text_kind = 'description'
   AND variant_description.ordinal = 1;

DROP VIEW IF EXISTS v_granted_proficiencies;
CREATE VIEW v_granted_proficiencies AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    g.grant_level,
    proficiency.element_id AS proficiency_element_id,
    proficiency.aurora_id AS proficiency_aurora_id,
    proficiency.name AS proficiency_name,
    proficiency_rec.package_key AS proficiency_package_key,
    proficiency_sf.relative_path AS proficiency_source_path
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN elements AS proficiency
    ON proficiency.element_id = g.target_element_id
JOIN resolved_elements_cache AS proficiency_rec
    ON proficiency_rec.winning_element_id = proficiency.element_id
JOIN source_files AS proficiency_sf
    ON proficiency_sf.source_file_id = proficiency.source_file_id
JOIN element_types AS proficiency_type
    ON proficiency_type.element_type_id = proficiency.element_type_id
WHERE proficiency_type.type_name = 'Proficiency';

DROP VIEW IF EXISTS v_granted_languages;
CREATE VIEW v_granted_languages AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    g.grant_id,
    g.ordinal AS grant_ordinal,
    g.grant_level,
    language.element_id AS language_element_id,
    language.aurora_id AS language_aurora_id,
    language.name AS language_name,
    language_rec.package_key AS language_package_key,
    language_sf.relative_path AS language_source_path
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN elements AS language
    ON language.element_id = g.target_element_id
JOIN resolved_elements_cache AS language_rec
    ON language_rec.winning_element_id = language.element_id
JOIN source_files AS language_sf
    ON language_sf.source_file_id = language.source_file_id
JOIN element_types AS language_type
    ON language_type.element_type_id = language.element_type_id
WHERE language_type.type_name = 'Language';

DROP VIEW IF EXISTS v_selectable_options;
CREATE VIEW v_selectable_options AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    'element' AS option_kind,
    option_element.element_id AS option_element_id,
    option_element.aurora_id AS option_aurora_id,
    option_element.name AS option_name,
    option_rec.package_key AS option_package_key,
    option_sf.relative_path AS option_source_path,
    option_type.type_name AS option_type_name,
    NULL AS option_text,
    GROUP_CONCAT(DISTINCT sol.match_kind) AS match_kinds,
    GROUP_CONCAT(DISTINCT st.support_text) AS support_tags
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN select_option_links AS sol
    ON sol.select_id = s.select_id
JOIN elements AS option_element
    ON option_element.element_id = sol.option_element_id
JOIN resolved_elements_cache AS option_rec
    ON option_rec.winning_element_id = option_element.element_id
JOIN source_files AS option_sf
    ON option_sf.source_file_id = option_element.source_file_id
JOIN element_types AS option_type
    ON option_type.element_type_id = option_element.element_type_id
LEFT JOIN support_tags AS st
    ON st.support_tag_id = sol.support_tag_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_rec.package_key,
    owner_sf.relative_path,
    owner_type.type_name,
    s.select_id,
    s.name_text,
    s.select_type,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    option_element.element_id,
    option_element.aurora_id,
    option_element.name,
    option_rec.package_key,
    option_sf.relative_path,
    option_type.type_name

UNION ALL

SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_rec.package_key AS owner_package_key,
    owner_sf.relative_path AS owner_source_path,
    owner_type.type_name AS owner_type_name,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.select_level,
    s.number_to_choose,
    s.is_optional,
    'text-choice' AS option_kind,
    NULL AS option_element_id,
    NULL AS option_aurora_id,
    NULL AS option_name,
    NULL AS option_package_key,
    NULL AS option_source_path,
    NULL AS option_type_name,
    si.item_text AS option_text,
    NULL AS match_kinds,
    NULL AS support_tags
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN resolved_elements_cache AS owner_rec
    ON owner_rec.winning_element_id = owner.element_id
JOIN source_files AS owner_sf
    ON owner_sf.source_file_id = owner.source_file_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN select_items AS si
    ON si.select_id = s.select_id
WHERE si.option_kind = 'text-choice';

CREATE VIEW IF NOT EXISTS v_grant_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    g.grant_id,
    g.ordinal,
    g.grant_type,
    g.name_text,
    g.target_aurora_id,
    g.target_semantic_key,
    g.target_semantic_kind,
    g.target_semantic_name,
    g.raw_xml,
    g.grant_level,
    g.requirements_text,
    target.element_id AS target_element_id,
    target.name AS target_name,
    target_type.type_name AS target_type_name
FROM grants AS g
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = g.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN elements AS target
    ON target.element_id = g.target_element_id
LEFT JOIN element_types AS target_type
    ON target_type.element_type_id = target.element_type_id;

CREATE VIEW IF NOT EXISTS v_selection_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    s.select_id,
    s.name_text,
    s.select_type,
    s.supports_text,
    s.select_level,
    s.number_to_choose,
    s.default_choice_text,
    s.is_optional,
    s.requirements_text,
    GROUP_CONCAT(ss.support_text, ' | ') AS supports_summary
FROM selects AS s
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN select_supports AS ss
    ON ss.select_id = s.select_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    s.select_id,
    s.name_text,
    s.select_type,
    s.supports_text,
    s.select_level,
    s.number_to_choose,
    s.default_choice_text,
    s.is_optional,
    s.requirements_text;

CREATE VIEW IF NOT EXISTS v_select_option_candidates AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    s.select_level,
    st.support_text,
    st.support_kind,
    sol.match_kind,
    option_element.element_id AS option_element_id,
    option_element.aurora_id AS option_aurora_id,
    option_element.name AS option_name,
    option_type.type_name AS option_type_name
FROM select_option_links AS sol
JOIN selects AS s
    ON s.select_id = sol.select_id
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
JOIN support_tags AS st
    ON st.support_tag_id = sol.support_tag_id
JOIN elements AS option_element
    ON option_element.element_id = sol.option_element_id
JOIN element_types AS option_type
    ON option_type.element_type_id = option_element.element_type_id;

CREATE VIEW IF NOT EXISTS v_setter_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    ss.owner_kind AS setter_owner_kind,
    se.setter_entry_id,
    se.ordinal,
    se.setter_name,
    se.setter_value,
    GROUP_CONCAT(sea.attribute_name || '=' || COALESCE(sea.attribute_value, ''), ' | ') AS attributes_summary
FROM setter_entries AS se
JOIN setter_scopes AS ss
    ON ss.setter_scope_id = se.setter_scope_id
JOIN elements AS owner
    ON owner.element_id = ss.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN setter_entry_attributes AS sea
    ON sea.setter_entry_id = se.setter_entry_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    ss.owner_kind,
    se.setter_entry_id,
    se.ordinal,
    se.setter_name,
    se.setter_value;

CREATE VIEW IF NOT EXISTS v_extract_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    ex.description_text,
    ei.extract_item_id,
    ei.ordinal,
    ei.item_text,
    ei.target_aurora_id,
    linked.aurora_id AS linked_aurora_id,
    linked.name AS linked_name,
    ei.amount_text,
    GROUP_CONCAT(eia.attribute_name || '=' || COALESCE(eia.attribute_value, ''), ' | ') AS attributes_summary
FROM element_extracts AS ex
JOIN elements AS owner
    ON owner.element_id = ex.element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN element_extract_items AS ei
    ON ei.element_id = ex.element_id
LEFT JOIN elements AS linked
    ON linked.element_id = ei.linked_element_id
LEFT JOIN element_extract_item_attributes AS eia
    ON eia.extract_item_id = ei.extract_item_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    ex.description_text,
    ei.extract_item_id,
    ei.ordinal,
    ei.item_text,
    ei.target_aurora_id,
    linked.aurora_id,
    linked.name,
    ei.amount_text;

CREATE VIEW IF NOT EXISTS v_element_block_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    eb.element_block_id,
    eb.ordinal,
    eb.block_name,
    eb.body_text,
    eb.raw_xml,
    GROUP_CONCAT(eba.attribute_name || '=' || COALESCE(eba.attribute_value, ''), ' | ') AS attributes_summary
FROM element_blocks AS eb
JOIN elements AS owner
    ON owner.element_id = eb.element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN element_block_attributes AS eba
    ON eba.element_block_id = eb.element_block_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    eb.element_block_id,
    eb.ordinal,
    eb.block_name,
    eb.body_text,
    eb.raw_xml;

CREATE VIEW IF NOT EXISTS v_element_text_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    etx.element_text_id,
    etx.text_kind,
    etx.ordinal,
    etx.level,
    etx.display,
    etx.alt_text,
    etx.action_text,
    etx.usage_text,
    etx.body,
    etm.content_format,
    etm.raw_xml
FROM element_texts AS etx
JOIN elements AS owner
    ON owner.element_id = etx.element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN element_text_markup AS etm
    ON etm.element_text_id = etx.element_text_id;

CREATE VIEW IF NOT EXISTS v_expression_usage_loader AS
SELECT
    eu.expression_usage_id,
    eu.owner_kind,
    eu.owner_id,
    eu.field_name,
    eu.ordinal,
    eu.source_text,
    e.expression_id,
    e.parse_status,
    e.error_text,
    root.node_kind AS root_node_kind,
    root.value_type AS root_value_type,
    root.value_text AS root_value_text
FROM expression_usages AS eu
JOIN expressions AS e
    ON e.expression_id = eu.expression_id
LEFT JOIN expression_nodes AS root
    ON root.expression_id = e.expression_id
   AND root.parent_node_id IS NULL;

CREATE VIEW IF NOT EXISTS v_select_item_loader AS
SELECT
    owner.element_id AS owner_element_id,
    owner.aurora_id AS owner_aurora_id,
    owner.name AS owner_name,
    owner_type.type_name AS owner_type_name,
    rs.owner_kind AS rule_owner_kind,
    s.select_id,
    s.name_text AS select_name,
    s.select_type,
    si.select_item_id,
    si.ordinal,
    si.item_text,
    si.target_aurora_id,
    linked.aurora_id AS linked_aurora_id,
    linked.name AS linked_name,
    GROUP_CONCAT(sia.attribute_name || '=' || COALESCE(sia.attribute_value, ''), ' | ') AS attributes_summary
FROM select_items AS si
JOIN selects AS s
    ON s.select_id = si.select_id
JOIN rule_scopes AS rs
    ON rs.rule_scope_id = s.rule_scope_id
JOIN elements AS owner
    ON owner.element_id = rs.owner_element_id
JOIN element_types AS owner_type
    ON owner_type.element_type_id = owner.element_type_id
LEFT JOIN elements AS linked
    ON linked.element_id = si.linked_element_id
LEFT JOIN select_item_attributes AS sia
    ON sia.select_item_id = si.select_item_id
GROUP BY
    owner.element_id,
    owner.aurora_id,
    owner.name,
    owner_type.type_name,
    rs.owner_kind,
    s.select_id,
    s.name_text,
    s.select_type,
    si.select_item_id,
    si.ordinal,
    si.item_text,
    si.target_aurora_id,
    linked.aurora_id,
    linked.name;

CREATE VIEW IF NOT EXISTS v_support_tag_roles AS
SELECT
    summary.support_tag_id,
    summary.support_text,
    summary.support_kind AS primary_kind,
    summary.supporting_element_count,
    summary.supporting_select_count,
    summary.candidate_option_count,
    summary.resolved_target_count,
    summary.has_primary_parent,
    CASE WHEN summary.has_primary_parent = 1 THEN 1 ELSE 0 END AS has_parent_role,
    CASE WHEN summary.candidate_option_count > 0 THEN 1 ELSE 0 END AS has_option_role,
    CASE WHEN summary.support_kind = 'broad-option-set' THEN 1 ELSE 0 END AS is_broad_option_role,
    CASE WHEN summary.support_kind = 'dynamic-expression' THEN 1 ELSE 0 END AS is_dynamic_expression,
    CASE
        WHEN summary.candidate_option_count = 0 THEN NULL
        WHEN summary.support_kind = 'broad-option-set' THEN 'broad-option-set'
        ELSE 'bounded-option-set'
    END AS option_role_kind
FROM v_support_tag_summary AS summary;

CREATE VIEW IF NOT EXISTS v_spell_loader AS
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    sp.spell_level,
    sp.school_name,
    sp.casting_time_text,
    sp.range_text,
    sp.duration_text,
    sp.has_verbal,
    sp.has_somatic,
    sp.has_material,
    sp.material_text,
    sp.is_concentration,
    sp.is_ritual,
    sp.attack_type,
    sp.damage_type_text,
    sp.damage_formula_text,
    sp.dc_ability_name,
    sp.dc_success_text,
    sp.source_url,
    GROUP_CONCAT(sa.access_text, ' | ') AS access_summary,
    sheet.body AS sheet_text,
    body.body AS description_text,
    sheet_markup.raw_xml AS sheet_raw_xml,
    body_markup.raw_xml AS description_raw_xml,
    summary_markup.raw_xml AS summary_raw_xml
FROM spells AS sp
JOIN elements AS e
    ON e.element_id = sp.element_id
LEFT JOIN spell_access AS sa
    ON sa.spell_element_id = sp.element_id
LEFT JOIN element_texts AS sheet
    ON sheet.element_id = e.element_id
   AND sheet.text_kind = 'sheet'
   AND sheet.ordinal = 1
LEFT JOIN element_texts AS body
    ON body.element_id = e.element_id
   AND body.text_kind = 'description'
   AND body.ordinal = 1
LEFT JOIN element_text_markup AS sheet_markup
    ON sheet_markup.element_text_id = sheet.element_text_id
LEFT JOIN element_text_markup AS body_markup
    ON body_markup.element_text_id = body.element_text_id
LEFT JOIN element_texts AS summary
    ON summary.element_id = e.element_id
   AND summary.text_kind = 'summary'
   AND summary.ordinal = 1
LEFT JOIN element_text_markup AS summary_markup
    ON summary_markup.element_text_id = summary.element_text_id
GROUP BY
    e.element_id,
    e.aurora_id,
    e.name,
    sp.spell_level,
    sp.school_name,
    sp.casting_time_text,
    sp.range_text,
    sp.duration_text,
    sp.has_verbal,
    sp.has_somatic,
    sp.has_material,
    sp.material_text,
    sp.is_concentration,
    sp.is_ritual,
    sp.attack_type,
    sp.damage_type_text,
    sp.damage_formula_text,
    sp.dc_ability_name,
    sp.dc_success_text,
    sp.source_url,
    sheet.body,
    body.body,
    sheet_markup.raw_xml,
    body_markup.raw_xml,
    summary_markup.raw_xml;

CREATE VIEW IF NOT EXISTS v_character_core_elements AS
SELECT
    e.element_id,
    e.aurora_id,
    e.name,
    et.type_name,
    e.loader_priority,
    summary.body AS summary_text
FROM elements AS e
JOIN element_types AS et
    ON et.element_type_id = e.element_type_id
LEFT JOIN element_texts AS summary
    ON summary.element_id = e.element_id
   AND summary.text_kind IN ('summary', 'sheet', 'description')
   AND summary.ordinal = 1
WHERE et.type_name IN
(
    'Source',
    'Class',
    'Archetype',
    'Race',
    'Race Variant',
    'Dragonmark',
    'Sub Race',
    'Background',
    'Background Variant',
    'Feat',
    'Spell',
    'Language',
    'Proficiency'
)
ORDER BY e.loader_priority, et.type_name, e.name;

COMMIT;
