use std::time::Instant;
use std::env;

fn sieve(limit: usize) -> usize {
    let word_cnt = (limit + 64) / 64;
    let mut bits = vec![0u64; word_cnt];

    let mut i = 2;
    while i * i <= limit {
        if (bits[i >> 6] >> (i & 63)) & 1 == 0 {
            let mut j = i * i;
            while j <= limit {
                bits[j >> 6] |= 1u64 << (j & 63);
                j += i;
            }
        }
        i += 1;
    }

    let mut count = 0;
    for i in 2..=limit {
        if (bits[i >> 6] >> (i & 63)) & 1 == 0 {
            count += 1;
        }
    }
    count
}

fn main() {
    let args: Vec<String> = env::args().collect();
    let limit: usize = args.get(1)
        .and_then(|s| s.parse().ok())
        .unwrap_or(1_000_000);

    let _ = sieve(limit); // warmup
    let start = Instant::now();
    let result = sieve(limit);
    let us = start.elapsed().as_secs_f64() * 1_000_000.0;
    println!("Rust,{},{},{:.0}", limit, result, us);
}
