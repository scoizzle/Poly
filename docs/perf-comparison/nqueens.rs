use std::time::Instant;

fn nqueens() -> i32 {
    let mut total = 0;
    let all: i32 = 255;
    let mut cols = [0i32; 8];
    let mut ld = [0i32; 8];
    let mut rd = [0i32; 8];
    let mut tried = [0i32; 8];
    let mut row: i32 = 0;
    cols[0] = 0; ld[0] = 0; rd[0] = 0; tried[0] = 0;

    while row >= 0 {
        let r = row as usize;
        let avail = all & !(cols[r] | ld[r] | rd[r]) & !tried[r];
        if avail == 0 {
            tried[r] = 0;
            row -= 1;
            continue;
        }
        let bit = avail & -avail;
        tried[r] |= bit;
        if row == 7 {
            total += 1;
        } else {
            row += 1;
            let nr = row as usize;
            let pr = (row - 1) as usize;
            cols[nr] = cols[pr] | bit;
            ld[nr] = (ld[pr] | bit) << 1;
            rd[nr] = (rd[pr] | bit) >> 1;
            tried[nr] = 0;
        }
    }
    total
}

fn main() {
    let start = Instant::now();
    let result = nqueens();
    let us = start.elapsed().as_secs_f64() * 1_000_000.0;
    println!("Rust,8,{},{:.0}", result, us);
}
