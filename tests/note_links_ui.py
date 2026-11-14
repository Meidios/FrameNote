"""Check note links in a released desk and dispatch one to the real default browser."""
import json, threading, uuid, zipfile
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from playwright.sync_api import sync_playwright
from review_ui import fixtures, work, launch, stop, wait

received=threading.Event()
route="/"+uuid.uuid4().hex+"/releases?q=take&v=2"
class Probe(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path==route:received.set()
        self.send_response(200);self.send_header("Content-Type","text/plain; charset=utf-8")
        self.end_headers();self.wfile.write(b"FrameNote link check passed. You can close this tab.")
    def log_message(self,*args):pass

github="https://github.com/DM74164/TaskbarPopup/releases"
with ThreadingHTTPServer(("127.0.0.1",0),Probe) as server:
    threading.Thread(target=server.serve_forever,daemon=True).start()
    local=f"http://127.0.0.1:{server.server_port}{route}"
    text=f"参考：{github}。另见（{local}），以及 www.example.org。"
    with zipfile.ZipFile(work/"links.framenote","w") as z:
        with zipfile.ZipFile("examples/SampleReview.framenote") as old:
            review=json.loads(old.read("review.json"))
            review["notes"][0]["text"]=text
            z.writestr("review.json",json.dumps(review))
            media=next(name for name in old.namelist() if name.startswith("media/"))
            z.writestr(media,old.read(media))
    with sync_playwright() as pw:
        proc,page=launch(pw,work/"links.framenote")
        try:
            row=page.locator(".note").first
            links=row.locator("a");assert links.all_text_contents()==[github,local,"www.example.org"]
            assert row.locator(".noteBody").inner_text()==text
            assert links.first.evaluate("el=>el.getClientRects().length")>1
            page.locator(".note .stamp").last.click()
            wait(page,"selected===doc.notes[1].id && Math.abs(shown-doc.notes[1].startUs)<=2")
            state=page.evaluate("[selected,shown,location.href]")
            links.nth(1).click()
            assert received.wait(10),"No request from the default browser"
            assert page.evaluate("[selected,shown,location.href]")==state
            row.locator(".stamp").click()
            wait(page,"selected===doc.notes[0].id && Math.abs(shown-doc.notes[0].startUs)<=2")
            print("PASS: saved text unchanged, wrapped and multiple links, browser GET, selection and frame preserved")
        finally:stop(proc)
    server.shutdown()
