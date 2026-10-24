import {bindValue, useValue} from "cs2/api";
import mod from "../../mod.json";
import styles from "./cursors.module.scss";

interface Cursor {
    n: string;  // username
    a: string;  // activity, e.g. "zoning"
    x: number;  // % of screen width
    y: number;  // % of screen height
    c: string;  // colour
}

const cursorsBinding = bindValue<string>(mod.id, 'Cursors', '[]');

// Name labels over the other players' cursors (the rings are drawn in-world by CursorOverlaySystem)
export const CursorLabels = () => {
    const json = useValue(cursorsBinding);
    let cursors: Cursor[] = [];
    try {
        cursors = JSON.parse(json);
    } catch {
        cursors = [];
    }

    return (
        <div className={styles.layer}>
            {cursors.map(cursor => (
                <div key={cursor.n} className={styles.label}
                     style={{left: cursor.x + "%", top: cursor.y + "%", borderColor: cursor.c}}>
                    <span className={styles.name} style={{color: cursor.c}}>{cursor.n}</span>
                    {cursor.a && <span className={styles.activity}>{cursor.a}</span>}
                </div>
            ))}
        </div>
    );
}
