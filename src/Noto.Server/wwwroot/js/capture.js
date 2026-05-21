window.notoCapture = window.notoCapture || {};

let mediaRecorder = null;
let audioChunks = [];
let captureDotnetRef = null;

notoCapture.init = function(ref) {
    captureDotnetRef = ref;
};

// Voice recording
notoCapture.startRecording = async function() {
    try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        mediaRecorder = new MediaRecorder(stream, { mimeType: 'audio/webm;codecs=opus' });
        audioChunks = [];

        mediaRecorder.ondataavailable = (e) => {
            if (e.data.size > 0) audioChunks.push(e.data);
        };

        mediaRecorder.onstop = async () => {
            const blob = new Blob(audioChunks, { type: 'audio/webm' });
            const reader = new FileReader();
            reader.onloadend = () => {
                const base64 = reader.result.split(',')[1];
                captureDotnetRef.invokeMethodAsync('OnRecordingComplete', base64);
            };
            reader.readAsDataURL(blob);

            // Stop all tracks
            stream.getTracks().forEach(t => t.stop());
        };

        mediaRecorder.start();
        return true;
    } catch (err) {
        console.error('Recording failed:', err);
        return false;
    }
};

notoCapture.stopRecording = function() {
    if (mediaRecorder && mediaRecorder.state === 'recording') {
        mediaRecorder.stop();
    }
};

notoCapture.isRecording = function() {
    return mediaRecorder && mediaRecorder.state === 'recording';
};
