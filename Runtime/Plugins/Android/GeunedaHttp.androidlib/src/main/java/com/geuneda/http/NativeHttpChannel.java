package com.geuneda.http;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.SocketTimeoutException;
import java.net.URL;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ThreadFactory;

/**
 * C# NativeHttpTransport 가 JNI 로 부르는 요청 통로. 메서드는 메인 스레드에서 부르고 요청은 작업 스레드에서 HttpURLConnection 으로 보낸다.
 * start 와 copyResponse 는 넘겨받은 직접 버퍼를 돌아오기 전에 다 읽거나 쓴다(C# 은 돌아온 뒤 바로 그 버퍼를 다시 쓴다).
 * cancel 은 지금 요청을 떼어 내므로 끝나길 기다리지 않고 다음 start 를 불러도 된다.
 * 프레임 형식은 C# HttpBufferFrame 과 짝이다.
 */
public final class NativeHttpChannel {
    public static final int IDLE = 0;
    public static final int RUNNING = 1;
    public static final int DONE = 2;
    public static final int NETWORK_ERROR = 3;
    public static final int TIMEOUT = 4;
    public static final int CANCELLED = 5;
    /** 주소·메서드·헤더가 잘못돼 연결 전에 거절했다(C# 은 오용 예외로 바꾼다) */
    public static final int INVALID_REQUEST = 6;

    static final int FRAME_VERSION = 1;

    private static final int INITIAL_RESPONSE_CAPACITY = 16 * 1024;

    private static final ExecutorService EXECUTOR = Executors.newCachedThreadPool(new ThreadFactory() {
        @Override
        public Thread newThread(Runnable runnable) {
            Thread thread = new Thread(runnable, "geuneda-http");
            thread.setDaemon(true);
            return thread;
        }
    });

    private volatile Call current;

    /** 0 이면 시작, -1 이면 앞 요청이 아직 끝나지 않았다 */
    public int start(ByteBuffer frame, int length) {
        Call previous = current;
        if (previous != null && previous.state == RUNNING) {
            return -1;
        }

        byte[] bytes = new byte[length];
        ByteBuffer view = frame.duplicate();
        view.clear();
        view.get(bytes, 0, length);
        Call call = new Call(bytes);
        current = call;
        EXECUTOR.execute(call);
        return 0;
    }

    public int poll() {
        Call call = current;
        return call == null ? IDLE : call.state;
    }

    public int statusCode() {
        Call call = current;
        return call == null ? 0 : call.statusCode;
    }

    public int responseLength() {
        Call call = current;
        return call == null || call.state != DONE ? 0 : call.responseLength;
    }

    /** 응답 본문을 destination 앞에 쓰고 길이를 돌려준다. 끝난 요청이 없거나 버퍼가 모자라면 -1 */
    public int copyResponse(ByteBuffer destination) {
        Call call = current;
        if (call == null || call.state != DONE || destination.capacity() < call.responseLength) {
            return -1;
        }

        ByteBuffer view = destination.duplicate();
        view.clear();
        view.put(call.response, 0, call.responseLength);
        return call.responseLength;
    }

    public String error() {
        Call call = current;
        return call == null || call.error == null ? "" : call.error;
    }

    public void cancel() {
        Call call = current;
        current = null;
        if (call != null) {
            call.cancel();
        }
    }

    static Request parse(ByteBuffer frame) {
        ByteBuffer b = frame.duplicate().order(ByteOrder.BIG_ENDIAN);
        int version = b.getInt();
        if (version != FRAME_VERSION) {
            throw new IllegalArgumentException("Unknown frame version " + version);
        }

        Request request = new Request();
        request.method = readString(b);
        request.timeoutMs = b.getInt();
        request.url = readString(b);
        request.contentType = readString(b);
        int count = b.getInt();
        request.headerNames = new String[count];
        request.headerValues = new String[count];
        for (int i = 0; i < count; i++) {
            request.headerNames[i] = readString(b);
            request.headerValues[i] = readString(b);
        }

        int bodyLength = b.getInt();
        if (bodyLength >= 0) {
            request.body = new byte[bodyLength];
            b.get(request.body);
        }

        return request;
    }

    private static String readString(ByteBuffer b) {
        byte[] bytes = new byte[b.getInt()];
        b.get(bytes);
        return new String(bytes, StandardCharsets.UTF_8);
    }

    static final class Request {
        String method;
        int timeoutMs;
        String url;
        String contentType;
        String[] headerNames;
        String[] headerValues;
        byte[] body;
    }

    private static final class Call implements Runnable {
        private final byte[] frame;
        private volatile boolean cancelled;
        private volatile HttpURLConnection connection;

        // state 는 마지막에 쓴다(volatile). 그 앞의 값은 state 를 읽은 쪽에 보인다
        volatile int state = RUNNING;
        int statusCode;
        byte[] response;
        int responseLength;
        String error;

        Call(byte[] frame) {
            this.frame = frame;
        }

        void cancel() {
            cancelled = true;
            HttpURLConnection open = connection;
            if (open != null) {
                open.disconnect();
            }
        }

        @Override
        public void run() {
            Request request;
            HttpURLConnection open;
            try {
                // 여기까지는 연결하지 않는다. 실패는 요청이 잘못된 것이다
                request = parse(ByteBuffer.wrap(frame));
                open = configure(request);
            } catch (Exception e) {
                error = e.toString();
                state = cancelled ? CANCELLED : INVALID_REQUEST;
                return;
            }

            int result;
            connection = open;
            try {
                if (cancelled) {
                    throw new IOException("Cancelled");
                }

                if (request.body != null) {
                    OutputStream out = open.getOutputStream();
                    try {
                        out.write(request.body);
                    } finally {
                        out.close();
                    }
                }

                int status = open.getResponseCode();
                InputStream in = status >= 400 ? open.getErrorStream() : open.getInputStream();
                readBody(in, open.getContentLength());
                statusCode = status;
                result = DONE;
            } catch (SocketTimeoutException e) {
                error = e.toString();
                result = cancelled ? CANCELLED : TIMEOUT;
            } catch (Exception e) {
                error = e.toString();
                result = cancelled ? CANCELLED : NETWORK_ERROR;
            }

            if (result != DONE) {
                open.disconnect();
            }

            connection = null;
            state = result;
        }

        private static HttpURLConnection configure(Request request) throws IOException {
            URL url = new URL(request.url);
            // URL 은 65535 를 넘는 포트를 받아 두었다가 연결 때 실패한다. 연결 전에 오용으로 거른다
            if (url.getPort() < -1 || url.getPort() > 65535) {
                throw new IllegalArgumentException("Invalid port: " + url.getPort());
            }

            HttpURLConnection open = (HttpURLConnection) url.openConnection();
            open.setRequestMethod(request.method);
            open.setUseCaches(false);
            open.setConnectTimeout(request.timeoutMs);
            open.setReadTimeout(request.timeoutMs);
            for (int i = 0; i < request.headerNames.length; i++) {
                open.setRequestProperty(request.headerNames[i], request.headerValues[i]);
            }

            if (request.body != null) {
                if (!request.contentType.isEmpty()) {
                    open.setRequestProperty("Content-Type", request.contentType);
                }

                open.setDoOutput(true);
                open.setFixedLengthStreamingMode(request.body.length);
            }

            return open;
        }

        // 본문을 다 읽고 닫으면 연결을 다시 쓸 수 있다(disconnect 하지 않는다)
        private void readBody(InputStream in, int contentLength) throws IOException {
            response = new byte[contentLength > 0 ? contentLength : INITIAL_RESPONSE_CAPACITY];
            responseLength = 0;
            if (in == null) {
                return;
            }

            try {
                while (true) {
                    if (responseLength == response.length) {
                        byte[] grown = new byte[response.length * 2];
                        System.arraycopy(response, 0, grown, 0, responseLength);
                        response = grown;
                    }

                    int read = in.read(response, responseLength, response.length - responseLength);
                    if (read < 0) {
                        return;
                    }

                    responseLength += read;
                }
            } finally {
                in.close();
            }
        }
    }
}
