declare module pofile {
    function parse(data: string): PO;
    function load(fileName: string, callback: (err: NodeJS.ErrnoException | null, po: PO) => void): void;

    class PO {
        public comments: string[];
        public extractedComments: string[];
        public items: Item[];
        public headers: Partial<IHeaders>

        public save(filename: string, callback: (err?: NodeJS.ErrnoException) => void): void;
        public toString(): string;
    }

    interface IHeaders {
        'Project-Id-Version': string;
        'Report-Msgid-Bugs-To': string;
        'POT-Creation-Date': string;
        'PO-Revision-Date': string;
        'Last-Translator': string;
        'Language': string;
        'Language-Team': string;
        'Content-Type': string;
        'Content-Transfer-Encoding': string;
        'Plural-Forms': string;
    }

    class Item {
        public msgid: string;
        public msgctxt?: string;
        public references: string[];
        public msgid_plural?: string;
        public msgstr: string[];
        public comments: string[];
        public extractedComments: string[];
        public flags: { [flag: string]: boolean | undefined }
        private nplurals: number;
        private obsolete: boolean;

        public toString(): string;
    }
}

export = pofile
