use std::io::{BufRead, BufReader};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;
use std::thread;
use tauri::Manager;

struct ServerState {
    child: Mutex<Option<Child>>,
}

fn main() {
    run()
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .manage(ServerState {
            child: Mutex::new(None),
        })
        .setup(|app| {
            let window = app.get_webview_window("main").unwrap();

            match find_server_binary(app) {
                Some(exe_path) => {
                    println!("[ShimmerChat] Starting server: {}", exe_path.display());

                    match spawn_server(&exe_path) {
                        Ok((child, port)) => {
                            println!("[ShimmerChat] Server ready on port {}", port);
                            let state = app.state::<ServerState>();
                            *state.child.lock().unwrap() = Some(child);

                            let url = format!("http://127.0.0.1:{}", port);
                            window
                                .eval(&format!("window.location.replace('{}')", url))
                                .ok();
                        }
                        Err(e) => {
                            eprintln!("[ShimmerChat] Failed to start server: {}", e);
                            show_error(&window, &e);
                        }
                    }
                }
                None => {
                    println!("[ShimmerChat] No bundled server, assuming dev mode on :26735");
                    window
                        .eval("window.location.replace('http://127.0.0.1:26735')")
                        .ok();
                }
            }

            Ok(())
        })
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::Destroyed = event {
                let state = window.state::<ServerState>();
                let mut guard = state.child.lock().unwrap();
                if let Some(mut child) = guard.take() {
                    println!("[ShimmerChat] Stopping server...");
                    let _ = child.kill();
                    let _ = child.wait();
                }
            }
        })
        .run(tauri::generate_context!())
        .expect("error while running ShimmerChat");
}

fn show_error(window: &tauri::WebviewWindow, msg: &str) {
    let safe = msg
        .replace('\\', "\\\\")
        .replace('\'', "\\'")
        .replace('\n', "\\n");
    let js = format!(
        "document.body.innerHTML = '<div style=\"text-align:center;padding:40px;font-family:sans-serif;color:#e0e0e0;background:#1a1a2e;height:100vh;\"><h2>Failed to start server</h2><pre style=\"color:#f88;margin-top:20px;text-align:left;max-width:600px;margin-left:auto;margin-right:auto;\">{}</pre></div>'",
        safe
    );
    window.eval(&js).ok();
}

fn server_exe_name() -> &'static str {
    if cfg!(target_os = "windows") {
        "ShimmerChat.exe"
    } else {
        "ShimmerChat"
    }
}

fn find_server_binary(app: &tauri::App) -> Option<std::path::PathBuf> {
    let name = server_exe_name();

    // Production: bundled resources
    if let Ok(resource_dir) = app.path().resource_dir() {
        let bundled = resource_dir.join("shimmer-server").join(name);
        if bundled.exists() {
            return Some(bundled);
        }
    }

    // Side-by-side with the Tauri exe
    if let Ok(current_exe) = std::env::current_exe() {
        if let Some(exe_dir) = current_exe.parent() {
            let sidecar = exe_dir.join("shimmer-server").join(name);
            if sidecar.exists() {
                return Some(sidecar);
            }
        }
    }

    // Dev: look in src-tauri/binaries
    if let Ok(cwd) = std::env::current_dir() {
        let dev_path = cwd.join("binaries").join("shimmer-server").join(name);
        if dev_path.exists() {
            return Some(dev_path);
        }
    }

    None
}

fn spawn_server(exe_path: &std::path::Path) -> Result<(Child, u16), String> {
    let working_dir = exe_path.parent().unwrap_or(std::path::Path::new("."));

    let mut child = Command::new(exe_path)
        .current_dir(working_dir)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| format!("Cannot spawn server: {}", e))?;

    let stdout = child.stdout.take().ok_or("No stdout pipe")?;
    let stderr = child.stderr.take().ok_or("No stderr pipe")?;

    // Drain stderr in background so it doesn't block
    thread::spawn(move || {
        for line in BufReader::new(stderr).lines() {
            if let Ok(l) = line {
                eprintln!("[server] {}", l);
            }
        }
    });

    // Read stdout line by line until we find SHIMMER_READY marker
    let mut reader = BufReader::new(stdout);
    let mut line = String::new();

    loop {
        // Check if process died
        match child.try_wait() {
            Ok(Some(status)) => {
                return Err(format!(
                    "Server exited with status {} before emitting SHIMMER_READY",
                    status
                ));
            }
            Err(e) => {
                return Err(format!("Server process error: {}", e));
            }
            Ok(None) => {}
        }

        line.clear();
        match reader.read_line(&mut line) {
            Ok(0) => {
                return Err("Server stdout closed before SHIMMER_READY".into());
            }
            Ok(_) => {
                print!("[server] {}", line);

                let trimmed = line.trim();
                if let Some(port_str) = trimmed.strip_prefix("SHIMMER_READY:http://127.0.0.1:") {
                    if let Ok(p) = port_str.parse::<u16>() {
                        return Ok((child, p));
                    }
                }
                if let Some(port_str) =
                    trimmed.strip_prefix("SHIMMER_READY:http://localhost:")
                {
                    if let Ok(p) = port_str.parse::<u16>() {
                        return Ok((child, p));
                    }
                }
            }
            Err(e) => {
                return Err(format!("Error reading server stdout: {}", e));
            }
        }
    }
}
