use std::time::Instant;
use std::env;

fn collatz(limit: i32) -> (i32, i32) {
    let mut best_n = 0;
    let mut max_len = 0;
    for n in 1..=limit {
        let mut m: i64 = n as i64;
        let mut len = 0;
        while m != 1 {
            if m % 2 == 0 {
                m /= 2;
            } else {
                m = m * 3 + 1;
            }
            len += 1;
        }
        if len > max_len {
            max_len = len;
            best_n = n;
        }
    }
    (best_n, max_len)
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let limit: i32 = args.get(1)
        .and_then(|s| s.parse().ok())
        .unwrap_or(10_000);

    let start = Instant::now();
    let (best_n, max_len) = collatz(limit);
    let ms = start.elapsed().as_secs_f64() * 1000.0;
    println!("Rust,{},{},{:.1}", limit, format!("{}:{}", best_n, max_len), ms);
}
