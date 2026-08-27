//! Tezuri — a desk for an author's entire publishing life.
//!
//! Architecture: a domain-driven, event-driven monolith. One library, one
//! grammar of change. Domains: spine (events, journal, writes, confinement,
//! jobs), publications, identity, articles, media, desk, consult, ship,
//! theme, render, and derive (background settling of derived artifacts).
//!
//! The grammar of change: every mutation of user content flows through
//! `propose -> show -> accept`, is written atomically, and appends an event to
//! the per-publication journal. Files are truth; every index and cache is a
//! lens that can be rebuilt from files at any time — and derived artifacts
//! heal lazily in the background.

pub mod articles;
pub mod consult;
pub mod derive;
pub mod desk;
pub mod identity;
pub mod media;
pub mod media_id;
pub mod publications;
pub mod render;
pub mod ship;
pub mod spine;
pub mod theme;
