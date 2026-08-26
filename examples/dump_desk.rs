//! Debug utility: rebuild the desk lens for a publication and print it as
//! JSON. Usage: cargo run -p tezuri --example dump_desk -- <publication-root>

fn main() {
    let mut args = std::env::args().skip(1);
    let path = args.next().unwrap_or_else(|| {
        eprintln!("usage: dump_desk <publication-root>");
        std::process::exit(2);
    });
    let d = tezuri::desk::Desk::rebuild(std::path::Path::new(&path)).unwrap();
    println!("{}", serde_json::to_string_pretty(&d).unwrap());
}
