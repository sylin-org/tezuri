
use tezuri::desk::Desk;
fn main() {
    let d = Desk::rebuild(std::path::Path::new(r"F:\Replica\NAS\Files\repo\github\lbotinelly\kintsugi-architecture")).unwrap();
    println!("{}", serde_json::to_string_pretty(&d).unwrap());
}
