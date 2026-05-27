window.socialVideotorUpload = {
    async uploadSelectedFile(fileInputId, uploadUrl, contentType) {
        const input = document.getElementById(fileInputId);
        const file = input?.files?.[0];
        if (!file) {
            throw new Error("No file selected.");
        }

        const response = await fetch(uploadUrl, {
            method: "PUT",
            headers: {
                "x-ms-blob-type": "BlockBlob",
                "Content-Type": contentType || file.type || "application/octet-stream"
            },
            body: file
        });

        if (!response.ok) {
            const body = await response.text();
            throw new Error(body || `Upload failed with status ${response.status}.`);
        }
    }
};
