package com.geuneda.http;

import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.ServerSocket;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;

/**
 * NativeHttpChannel 을 데스크톱 JDK 와 로컬 HttpServer 로 확인한다. 실행: Tests/Java~/run.sh
 * GOLDEN_FRAME 은 C# HttpBufferFrameTest 와 같은 바이트다.
 */
public final class NativeHttpChannelTest {
    static final String GOLDEN_FRAME =
        "0000000100000004504f5354000005dc0000000d68747470733a2f2f612e622f63000000106170706c69636174696f6e2f6a736f6e"
        + "0000000100000003582d410000000131000000027b7d";

    private static HttpServer server;
    private static String base;
    private static int failures;

    public static void main(String[] args) throws Exception {
        server = HttpServer.create(new InetSocketAddress("127.0.0.1", 0), 0);
        server.createContext("/echo", NativeHttpChannelTest::echo);
        server.createContext("/missing", exchange -> respond(exchange, 404, "nope".getBytes(StandardCharsets.UTF_8), false));
        server.createContext("/empty-error", exchange -> {
            exchange.sendResponseHeaders(500, -1);
            exchange.close();
        });
        server.createContext("/large", exchange -> respond(exchange, 200, large(), true));
        server.createContext("/slow", exchange -> {
            sleep(2000);
            respond(exchange, 200, "late".getBytes(StandardCharsets.UTF_8), false);
        });
        server.setExecutor(java.util.concurrent.Executors.newCachedThreadPool());
        server.start();
        base = "http://127.0.0.1:" + server.getAddress().getPort();

        run("golden frame parses", NativeHttpChannelTest::goldenFrame);
        run("post echoes headers and body", NativeHttpChannelTest::postEcho);
        run("get without body", NativeHttpChannelTest::getWithoutBody);
        run("404 body is a response", NativeHttpChannelTest::notFound);
        run("500 without body", NativeHttpChannelTest::emptyError);
        run("chunked large response grows", NativeHttpChannelTest::largeResponse);
        run("small destination refused", NativeHttpChannelTest::smallDestination);
        run("connection refused is network error", NativeHttpChannelTest::refused);
        run("read timeout", NativeHttpChannelTest::timeout);
        run("cancel detaches and allows next start", NativeHttpChannelTest::cancelThenStart);
        run("start while running refused", NativeHttpChannelTest::busy);
        run("bad port is invalid request", NativeHttpChannelTest::badPort);
        run("newline header is invalid request", NativeHttpChannelTest::badHeader);

        server.stop(0);
        if (failures > 0) {
            System.out.println(failures + " failed");
            System.exit(1);
        }

        System.out.println("all passed");
        System.exit(0);
    }

    static void goldenFrame() {
        NativeHttpChannel.Request request = NativeHttpChannel.parse(ByteBuffer.wrap(hex(GOLDEN_FRAME)));
        check("POST".equals(request.method), "method " + request.method);
        check(request.timeoutMs == 1500, "timeout " + request.timeoutMs);
        check("https://a.b/c".equals(request.url), "url " + request.url);
        check("application/json".equals(request.contentType), "content type " + request.contentType);
        check(request.headerNames.length == 1 && "X-A".equals(request.headerNames[0]) && "1".equals(request.headerValues[0]), "headers");
        check(Arrays.equals("{}".getBytes(StandardCharsets.UTF_8), request.body), "body");
    }

    static void postEcho() throws Exception {
        byte[] body = "{\"a\":\"é\"}".getBytes(StandardCharsets.UTF_8);
        Done done = send(frame("POST", 5000, base + "/echo", "application/json", new String[] {"X-Test", "yes"}, body));
        check(done.status == 201, "status " + done.status);
        String text = new String(done.body, StandardCharsets.UTF_8);
        check(text.equals("POST|application/json|yes|" + new String(body, StandardCharsets.UTF_8)), "echo " + text);
    }

    static void getWithoutBody() throws Exception {
        Done done = send(frame("GET", 5000, base + "/echo", "", new String[0], null));
        String text = new String(done.body, StandardCharsets.UTF_8);
        check(done.status == 201 && text.equals("GET|null|null|"), "echo " + text);
    }

    static void notFound() throws Exception {
        Done done = send(frame("GET", 5000, base + "/missing", "", new String[0], null));
        check(done.status == 404, "status " + done.status);
        check("nope".equals(new String(done.body, StandardCharsets.UTF_8)), "body");
    }

    static void emptyError() throws Exception {
        Done done = send(frame("GET", 5000, base + "/empty-error", "", new String[0], null));
        check(done.status == 500 && done.body.length == 0, "status " + done.status + " length " + done.body.length);
    }

    static void largeResponse() throws Exception {
        Done done = send(frame("GET", 5000, base + "/large", "", new String[0], null));
        check(done.status == 200, "status " + done.status);
        check(Arrays.equals(large(), done.body), "large body length " + done.body.length);
    }

    static void smallDestination() throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 5000, base + "/missing", "", new String[0], null));
        waitFor(channel);
        check(channel.copyResponse(ByteBuffer.allocateDirect(2)) == -1, "small buffer must be refused");
    }

    static void refused() throws Exception {
        int port;
        try (ServerSocket socket = new ServerSocket(0)) {
            port = socket.getLocalPort();
        }

        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 5000, "http://127.0.0.1:" + port + "/", "", new String[0], null));
        int state = waitFor(channel);
        check(state == NativeHttpChannel.NETWORK_ERROR, "state " + state);
        check(!channel.error().isEmpty(), "error message");
    }

    static void timeout() throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 200, base + "/slow", "", new String[0], null));
        int state = waitFor(channel);
        check(state == NativeHttpChannel.TIMEOUT, "state " + state);
    }

    static void cancelThenStart() throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 5000, base + "/slow", "", new String[0], null));
        sleep(100);
        channel.cancel();
        check(channel.poll() == NativeHttpChannel.IDLE, "idle after cancel");
        start(channel, frame("GET", 5000, base + "/missing", "", new String[0], null));
        int state = waitFor(channel);
        check(state == NativeHttpChannel.DONE && channel.statusCode() == 404, "next request state " + state);
    }

    static void busy() throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 5000, base + "/slow", "", new String[0], null));
        ByteBuffer next = direct(frame("GET", 5000, base + "/missing", "", new String[0], null));
        check(channel.start(next, next.capacity()) == -1, "second start must be refused");
        channel.cancel();
    }

    static void badPort() throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 5000, "https://h:bad/", "", new String[0], null));
        int state = waitFor(channel);
        check(state == NativeHttpChannel.INVALID_REQUEST, "state " + state + " " + channel.error());

        start(channel, frame("GET", 5000, "http://127.0.0.1:65536/", "", new String[0], null));
        state = waitFor(channel);
        check(state == NativeHttpChannel.INVALID_REQUEST, "port 65536 state " + state + " " + channel.error());
    }

    static void badHeader() throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame("GET", 5000, base + "/echo", "", new String[] {"X-Test", "a\r\nX-Evil: 1"}, null));
        int state = waitFor(channel);
        check(state == NativeHttpChannel.INVALID_REQUEST, "state " + state + " " + channel.error());
    }

    // C# HttpBufferFrame 과 같은 형식(테스트용)
    static byte[] frame(String method, int timeoutMs, String url, String contentType, String[] headers, byte[] body) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        writeInt(out, NativeHttpChannel.FRAME_VERSION);
        writeString(out, method);
        writeInt(out, timeoutMs);
        writeString(out, url);
        writeString(out, contentType);
        writeInt(out, headers.length / 2);
        for (String header : headers) {
            writeString(out, header);
        }

        writeInt(out, body == null ? -1 : body.length);
        if (body != null) {
            out.write(body);
        }

        return out.toByteArray();
    }

    static Done send(byte[] frame) throws Exception {
        NativeHttpChannel channel = new NativeHttpChannel();
        start(channel, frame);
        int state = waitFor(channel);
        if (state != NativeHttpChannel.DONE) {
            throw new AssertionError("state " + state + " " + channel.error());
        }

        Done done = new Done();
        done.status = channel.statusCode();
        ByteBuffer destination = ByteBuffer.allocateDirect(Math.max(1, channel.responseLength()));
        int length = channel.copyResponse(destination);
        done.body = new byte[length];
        destination.get(done.body, 0, length);
        return done;
    }

    static void start(NativeHttpChannel channel, byte[] frame) {
        ByteBuffer buffer = direct(frame);
        int started = channel.start(buffer, frame.length);
        if (started != 0) {
            throw new AssertionError("start " + started);
        }

        // C# 이 돌아온 뒤 버퍼를 다시 쓰는 것처럼 지운다
        buffer.clear();
        buffer.put(new byte[buffer.capacity()]);
    }

    static ByteBuffer direct(byte[] bytes) {
        ByteBuffer buffer = ByteBuffer.allocateDirect(bytes.length + 16);
        buffer.put(bytes);
        return buffer;
    }

    static int waitFor(NativeHttpChannel channel) {
        long end = System.currentTimeMillis() + 10000;
        while (channel.poll() == NativeHttpChannel.RUNNING) {
            if (System.currentTimeMillis() > end) {
                throw new AssertionError("still running");
            }

            sleep(5);
        }

        return channel.poll();
    }

    static void echo(HttpExchange exchange) throws IOException {
        byte[] body = readAll(exchange.getRequestBody());
        String text = exchange.getRequestMethod() + "|" + exchange.getRequestHeaders().getFirst("Content-Type") + "|"
            + exchange.getRequestHeaders().getFirst("X-Test") + "|" + new String(body, StandardCharsets.UTF_8);
        respond(exchange, 201, text.getBytes(StandardCharsets.UTF_8), false);
    }

    static void respond(HttpExchange exchange, int status, byte[] body, boolean chunked) throws IOException {
        exchange.sendResponseHeaders(status, chunked ? 0 : body.length);
        try (OutputStream out = exchange.getResponseBody()) {
            out.write(body);
        }
    }

    static byte[] large() {
        byte[] bytes = new byte[100 * 1024 + 7];
        for (int i = 0; i < bytes.length; i++) {
            bytes[i] = (byte) (i * 31);
        }

        return bytes;
    }

    static byte[] readAll(InputStream in) throws IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = in.read(buffer)) > 0) {
            out.write(buffer, 0, read);
        }

        return out.toByteArray();
    }

    static void writeInt(ByteArrayOutputStream out, int value) {
        out.write(value >>> 24);
        out.write(value >>> 16);
        out.write(value >>> 8);
        out.write(value);
    }

    static void writeString(ByteArrayOutputStream out, String value) throws IOException {
        byte[] bytes = value.getBytes(StandardCharsets.UTF_8);
        writeInt(out, bytes.length);
        out.write(bytes);
    }

    static byte[] hex(String text) {
        byte[] bytes = new byte[text.length() / 2];
        for (int i = 0; i < bytes.length; i++) {
            bytes[i] = (byte) Integer.parseInt(text.substring(i * 2, i * 2 + 2), 16);
        }

        return bytes;
    }

    static void sleep(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError(message);
        }
    }

    static void run(String name, ThrowingRunnable test) {
        try {
            test.run();
            System.out.println("PASS " + name);
        } catch (Throwable e) {
            failures++;
            System.out.println("FAIL " + name + ": " + e);
        }
    }

    interface ThrowingRunnable {
        void run() throws Exception;
    }

    static final class Done {
        int status;
        byte[] body;
    }
}
