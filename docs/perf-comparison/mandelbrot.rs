use std::time::Instant;

fn mandelbrot() -> i64 {
    let mut total: i64 = 0;
    for y in 0..128 {
        let cy = ((y - 64) as i64) * 8;
        for x in 0..128 {
            let cx = ((x - 64) as i64) * 8;
            let mut zx: i64 = 0;
            let mut zy: i64 = 0;
            let mut iter = 0;
            while iter < 256 {
                let zx2 = (zx * zx) >> 8;
                let zy2 = (zy * zy) >> 8;
                if (zx2 + zy2) > 1024 { break; }
                zy = ((zx * zy) >> 7) + cy;
                zx = zx2 - zy2 + cx;
                iter += 1;
            }
            total += iter as i64;
        }
    }
    total
}

fn main() {
    let start = Instant::now();
    let result = mandelbrot();
    let us = start.elapsed().as_secs_f64() * 1_000_000.0;
    println!("Rust,128,{},{:.0}", result, us);
}
