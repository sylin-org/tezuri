//!  The presentation pipeline: articles + space identity in, static pages out.

mod assets;
mod assets_templates;
mod assets_theme;
mod behaviors;
mod emit;
mod gather;
mod library;
mod markdown;
mod pipeline;
#[cfg(test)]
mod tests;
mod write_view;

pub(crate) use assets::*;
pub use assets_templates::*;
pub use assets_theme::*;
pub(crate) use behaviors::*;
pub(crate) use emit::*;
pub(crate) use gather::*;
pub use library::{download_asset, picker_apply, picker_history, picker_history_step, picker_list, PickerEntry};
pub(crate) use markdown::*;
pub(crate) use pipeline::*;

pub use assets::embedded_article_template;
pub use assets_templates::{read_template, write_template, ARTICLE_TEMPLATE_FILE};
pub use assets_theme::{presets, read_theme, theme_path, write_theme, Preset, THEME_FILE};
pub use emit::emit_render;
pub use emit::RENDER_DIR;
pub use pipeline::{render_article, render_article_warned, render_article_with};
pub use write_view::{
    compose_write_view, compose_write_view_with, Seg, SlotInstance, WriteCompose,
};
