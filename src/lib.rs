//! Tezuri — a desk for an author's entire publishing life.
//!
//! Architecture: a domain-driven, event-driven monolith. One binary, six domains
//! (publications, articles, media, desk, consult, ship), one grammar of change.
//!
//! The grammar of change: every mutation of user content flows through
//! `propose -> show -> accept`, is written atomically, and appends an event to
//! the per-publication journal. Files are truth; every index and cache is a
//! lens that can be rebuilt from files at any time.

pub mod articles;
pub mod consult;
pub mod desk;
pub mod identity;
pub mod media;
pub mod media_id;
pub mod publications;
pub mod ship;
pub mod spine;
pub mod theme;
