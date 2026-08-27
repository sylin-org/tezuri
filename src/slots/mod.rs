//!  The template language: English slots, one grammar, one evaluation.

mod article;
mod catalog;
mod ctx;
mod evaluate;
mod grammar;
#[cfg(test)]
mod tests;

pub(crate) use article::*;
pub(crate) use catalog::*;
pub use catalog::{registry, Control, Host, OptionSpec, SlotDef};
pub(crate) use ctx::*;
pub use ctx::{compose, compose_marked, Ctx, Heading, NeighborRef, Neighbors, Output};
pub(crate) use evaluate::*;
pub(crate) use grammar::*;
pub use grammar::{canonical_hints, parse_template, rewrite_slot_raw, Part, RawSlot};
