PRAGMA foreign_keys = ON;

BEGIN TRANSACTION;

-- Proof of concept:
-- This schema is optimized for character/feature loading first.
-- It captures the highest-value Aurora element families and the shared
-- grant/select/stat rule path that drives most character state.
--
-- Deliberate tradeoffs for the PoC:
-- - Descriptions are stored as text blobs instead of a normalized content DOM.
-- - Requirement/support expressions are stored as raw text instead of an AST.
-- - Lower-priority element families such as companions, deities, and lists
--   are intentionally deferred.
--
-- The next phase can normalize description markup and expression trees
-- without replacing the core element/rule shape defined here.

CREATE TABLE source_books
(
    source_book_id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE source_files
(
    source_file_id INTEGER PRIMARY KEY,
    relative_path TEXT NOT NULL UNIQUE,
    package_name TEXT,
    package_description TEXT,
    version_text TEXT,
    update_file_name TEXT,
    update_url TEXT,
    author_name TEXT,
    author_url TEXT
);

CREATE TABLE element_types
(
    element_type_id INTEGER PRIMARY KEY,
    type_name TEXT NOT NULL UNIQUE,
    loader_family TEXT NOT NULL
);

CREATE TABLE elements
(
    element_id INTEGER PRIMARY KEY,
    aurora_id TEXT NOT NULL,
    element_type_id INTEGER NOT NULL REFERENCES element_types(element_type_id),
    source_book_id INTEGER REFERENCES source_books(source_book_id),
    source_file_id INTEGER REFERENCES source_files(source_file_id),
    name TEXT NOT NULL,
    slug TEXT NOT NULL,
    compendium_display INTEGER NOT NULL DEFAULT 1 CHECK (compendium_display IN (0, 1)),
    loader_priority INTEGER NOT NULL DEFAULT 100,
    created_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX ix_elements_type_name ON elements(element_type_id, name);
CREATE INDEX ix_elements_source_name ON elements(source_book_id, name);
CREATE INDEX ix_elements_slug ON elements(slug);
CREATE INDEX ix_elements_aurora_id ON elements(aurora_id);

CREATE TABLE element_texts
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

CREATE INDEX ix_element_texts_kind ON element_texts(element_id, text_kind, ordinal);

CREATE TABLE element_supports
(
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_text TEXT NOT NULL,
    PRIMARY KEY (element_id, ordinal)
);

CREATE INDEX ix_element_supports_text ON element_supports(support_text);

CREATE TABLE element_requirements
(
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    requirement_text TEXT NOT NULL,
    PRIMARY KEY (element_id, ordinal)
);

CREATE TABLE support_tags
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

CREATE INDEX ix_support_tags_normalized ON support_tags(normalized_text);

CREATE TABLE element_support_links
(
    element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_tag_id INTEGER NOT NULL REFERENCES support_tags(support_tag_id),
    linked_element_id INTEGER REFERENCES elements(element_id),
    resolution_kind TEXT NOT NULL,
    is_primary_parent INTEGER NOT NULL DEFAULT 0 CHECK (is_primary_parent IN (0, 1)),
    PRIMARY KEY (element_id, ordinal)
);

CREATE INDEX ix_element_support_links_tag ON element_support_links(support_tag_id, element_id);
CREATE INDEX ix_element_support_links_target ON element_support_links(linked_element_id, is_primary_parent);

CREATE TABLE classes
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    hit_die TEXT,
    short_text TEXT
);

CREATE TABLE class_multiclass
(
    class_element_id INTEGER PRIMARY KEY REFERENCES classes(element_id) ON DELETE CASCADE,
    multiclass_aurora_id TEXT,
    prerequisite_text TEXT,
    requirements_text TEXT,
    proficiencies_text TEXT
);

CREATE TABLE spellcasting_profiles
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

CREATE TABLE archetypes
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    parent_class_element_id INTEGER REFERENCES classes(element_id),
    parent_support_text TEXT
);

CREATE TABLE races
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    names_format_text TEXT
);

CREATE TABLE race_name_groups
(
    race_element_id INTEGER NOT NULL REFERENCES races(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    name_group_type TEXT NOT NULL,
    name_value TEXT NOT NULL,
    PRIMARY KEY (race_element_id, ordinal)
);

CREATE INDEX ix_race_name_groups_type ON race_name_groups(race_element_id, name_group_type);

CREATE TABLE subraces
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    race_element_id INTEGER REFERENCES races(element_id),
    parent_support_text TEXT
);

CREATE TABLE backgrounds
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE
);

CREATE TABLE feats
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    allow_duplicate INTEGER CHECK (allow_duplicate IN (0, 1))
);

CREATE TABLE spells
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

CREATE INDEX ix_spells_level_school ON spells(spell_level, school_name);

CREATE TABLE spell_access
(
    spell_element_id INTEGER NOT NULL REFERENCES spells(element_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    access_text TEXT NOT NULL,
    PRIMARY KEY (spell_element_id, ordinal)
);

CREATE INDEX ix_spell_access_text ON spell_access(access_text);

CREATE TABLE languages
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    script_text TEXT,
    speakers_text TEXT,
    is_standard INTEGER NOT NULL DEFAULT 0 CHECK (is_standard IN (0, 1)),
    is_exotic INTEGER NOT NULL DEFAULT 0 CHECK (is_exotic IN (0, 1)),
    is_secret INTEGER NOT NULL DEFAULT 0 CHECK (is_secret IN (0, 1))
);

CREATE TABLE proficiencies
(
    element_id INTEGER PRIMARY KEY REFERENCES elements(element_id) ON DELETE CASCADE,
    proficiency_group TEXT,
    proficiency_subgroup TEXT
);

CREATE TABLE items
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

CREATE TABLE features
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
    parent_element_id INTEGER REFERENCES elements(element_id),
    parent_support_text TEXT,
    min_level INTEGER
);

CREATE INDEX ix_features_parent ON features(parent_element_id, min_level);
CREATE INDEX ix_features_kind ON features(feature_kind, min_level);

-- Rule scopes let the same grant/select/stat tables be reused for an
-- element's main rules and a class multiclass block without polymorphic FKs.
CREATE TABLE rule_scopes
(
    rule_scope_id INTEGER PRIMARY KEY,
    owner_kind TEXT NOT NULL CHECK (owner_kind IN ('element', 'class-multiclass')),
    owner_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    scope_key TEXT NOT NULL,
    UNIQUE (owner_kind, owner_element_id, scope_key)
);

CREATE TABLE grants
(
    grant_id INTEGER PRIMARY KEY,
    rule_scope_id INTEGER NOT NULL REFERENCES rule_scopes(rule_scope_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    grant_type TEXT NOT NULL,
    target_aurora_id TEXT,
    target_element_id INTEGER REFERENCES elements(element_id),
    name_text TEXT,
    grant_level INTEGER,
    requirements_text TEXT,
    UNIQUE (rule_scope_id, ordinal)
);

CREATE INDEX ix_grants_target ON grants(target_aurora_id, grant_type);
CREATE INDEX ix_grants_owner_level ON grants(rule_scope_id, grant_level);

CREATE TABLE selects
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
    spellcasting_profile_id INTEGER REFERENCES spellcasting_profiles(spellcasting_profile_id),
    requirements_text TEXT,
    UNIQUE (rule_scope_id, ordinal)
);

CREATE INDEX ix_selects_type ON selects(select_type, select_level);

CREATE TABLE select_supports
(
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_text TEXT NOT NULL,
    PRIMARY KEY (select_id, ordinal)
);

CREATE INDEX ix_select_supports_text ON select_supports(support_text);

CREATE TABLE select_support_links
(
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    support_tag_id INTEGER NOT NULL REFERENCES support_tags(support_tag_id),
    linked_element_id INTEGER REFERENCES elements(element_id),
    resolution_kind TEXT NOT NULL,
    PRIMARY KEY (select_id, ordinal)
);

CREATE INDEX ix_select_support_links_tag ON select_support_links(support_tag_id, select_id);
CREATE INDEX ix_select_support_links_target ON select_support_links(linked_element_id);

CREATE TABLE select_option_links
(
    select_id INTEGER NOT NULL REFERENCES selects(select_id) ON DELETE CASCADE,
    option_element_id INTEGER NOT NULL REFERENCES elements(element_id) ON DELETE CASCADE,
    support_tag_id INTEGER NOT NULL REFERENCES support_tags(support_tag_id),
    match_kind TEXT NOT NULL,
    PRIMARY KEY (select_id, option_element_id, support_tag_id, match_kind)
);

CREATE INDEX ix_select_option_links_select ON select_option_links(select_id, support_tag_id);
CREATE INDEX ix_select_option_links_option ON select_option_links(option_element_id, support_tag_id);

CREATE TABLE stats
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
    requirements_text TEXT,
    UNIQUE (rule_scope_id, ordinal)
);

CREATE INDEX ix_stats_name_level ON stats(stat_name, stat_level);

-- Seed the highest-value element types for character and feature loading.
INSERT INTO element_types (type_name, loader_family) VALUES ('Spell', 'spell');
INSERT INTO element_types (type_name, loader_family) VALUES ('Class', 'class');
INSERT INTO element_types (type_name, loader_family) VALUES ('Archetype', 'archetype');
INSERT INTO element_types (type_name, loader_family) VALUES ('Class Feature', 'feature');
INSERT INTO element_types (type_name, loader_family) VALUES ('Archetype Feature', 'feature');
INSERT INTO element_types (type_name, loader_family) VALUES ('Race', 'race');
INSERT INTO element_types (type_name, loader_family) VALUES ('Sub Race', 'race');
INSERT INTO element_types (type_name, loader_family) VALUES ('Racial Trait', 'feature');
INSERT INTO element_types (type_name, loader_family) VALUES ('Background', 'background');
INSERT INTO element_types (type_name, loader_family) VALUES ('Background Feature', 'feature');
INSERT INTO element_types (type_name, loader_family) VALUES ('Feat', 'feat');
INSERT INTO element_types (type_name, loader_family) VALUES ('Feat Feature', 'feature');
INSERT INTO element_types (type_name, loader_family) VALUES ('Ability Score Improvement', 'feature');
INSERT INTO element_types (type_name, loader_family) VALUES ('Language', 'language');
INSERT INTO element_types (type_name, loader_family) VALUES ('Proficiency', 'proficiency');
INSERT INTO element_types (type_name, loader_family) VALUES ('Item', 'item');
INSERT INTO element_types (type_name, loader_family) VALUES ('Weapon', 'item');
INSERT INTO element_types (type_name, loader_family) VALUES ('Armor', 'item');
INSERT INTO element_types (type_name, loader_family) VALUES ('Ammunition', 'item');
INSERT INTO element_types (type_name, loader_family) VALUES ('Mount', 'item');
INSERT INTO element_types (type_name, loader_family) VALUES ('Vehicle', 'item');
INSERT INTO element_types (type_name, loader_family) VALUES ('Magic Item', 'item');

-- Loader-centric views.

CREATE VIEW v_feature_loader AS
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

CREATE VIEW v_element_support_loader AS
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

CREATE VIEW v_support_tag_summary AS
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

CREATE VIEW v_class_feature_progression AS
SELECT
    c.element_id AS class_element_id,
    class_element.aurora_id AS class_aurora_id,
    class_element.name AS class_name,
    g.grant_level,
    feature_element.element_id AS feature_element_id,
    feature_element.aurora_id AS feature_aurora_id,
    feature_element.name AS feature_name,
    feature_type.type_name AS feature_type_name
FROM classes AS c
JOIN elements AS class_element
    ON class_element.element_id = c.element_id
JOIN rule_scopes AS rs
    ON rs.owner_kind = 'element'
   AND rs.owner_element_id = c.element_id
   AND rs.scope_key = 'element'
JOIN grants AS g
    ON g.rule_scope_id = rs.rule_scope_id
JOIN elements AS feature_element
    ON feature_element.aurora_id = g.target_aurora_id
JOIN element_types AS feature_type
    ON feature_type.element_type_id = feature_element.element_type_id
WHERE g.grant_type IN ('Class Feature', 'Archetype Feature')
ORDER BY class_name, g.grant_level, feature_name;

CREATE VIEW v_selection_loader AS
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

CREATE VIEW v_select_option_candidates AS
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

CREATE VIEW v_support_tag_roles AS
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

CREATE VIEW v_spell_loader AS
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
    body.body AS description_text
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
    body.body;

CREATE VIEW v_character_core_elements AS
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
    'Class',
    'Archetype',
    'Race',
    'Sub Race',
    'Background',
    'Feat',
    'Spell',
    'Language',
    'Proficiency'
)
ORDER BY e.loader_priority, et.type_name, e.name;

COMMIT;
